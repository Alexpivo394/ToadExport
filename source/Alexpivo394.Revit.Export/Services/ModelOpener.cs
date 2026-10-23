using System.IO;
using Alexpivo394.Revit.Export.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Nice3point.Revit.Toolkit;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Alexpivo394.Revit.Export.Services;

/// <param name="IsTransmitted">Открытый файл помечен как переданный (eTransmit) — при сохранении пометку нужно снять.</param>
/// <param name="LinksUnloaded">В копии связям поставлено «не загружать» — в сохранённом RVT это нужно вернуть.</param>
public sealed record OpenedModel(Document Document, bool IsTransmitted, bool LinksUnloaded);

/// <summary>
///     Открывает модель с отсоединением, не трогая исходник. С «Не загружать связи» модель сначала копируется
///     во временную папку (<c>CopyModel</c> умеет и Revit Server, и файл), а в копии всем связям RVT
///     ставится «не загружать» — открываются все рабочие наборы, но связи не грузятся.
/// </summary>
public sealed class ModelOpener(RevitApplication application)
{
    public OpenedModel Open(ModelJob job, ExportOptions options, TempWorkspace temp, IExportProgress progress, IExportLog log)
    {
        var path = job.SourcePath;
        var workshared = job.IsServer; // на Revit Server лежат только модели совместной работы
        var unloaded = 0;

        if (options.SkipLinks)
        {
            progress.Status("Копирование модели");
            log.Step(job.Name, "копирование во временную папку");
            var copy = Path.Combine(temp.Directory, job.Name);
            using (new RevitProgressRelay(application, progress, "Копирование модели"))
            {
                application.CopyModel(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), copy, true);
            }

            if (progress.ShouldAbort) throw new OperationCanceledException();

            unloaded = UnloadLinks(copy);
            log.Step(job.Name, $"связей отключено: {unloaded}");
            path = copy;
        }

        // Локальный файл есть — по нему узнаём совместную работу и версию. Путь Revit Server без копии так не прочитать:
        // версию там гарантирует сам сервер, он обслуживает одну версию Revit.
        if (!job.IsServer || options.SkipLinks)
        {
            var info = BasicFileInfo.Extract(path);
            workshared = info.IsWorkshared;
            VersionGuard.EnsureCanOpen(job.Name, ParseYear(info.Format), int.Parse(application.VersionNumber), options.AllowUpgrade);
        }

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        var transmitted = IsTransmitted(modelPath);
        var openOptions = new OpenOptions { Audit = options.Audit };
        if (workshared)
        {
            openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            openOptions.SetOpenWorksetsConfiguration(Worksets(modelPath, options.WorksetKeyword));
        }

        progress.Status("Открытие модели");
        log.Step(job.Name, "открытие" + (options.Audit ? " с проверкой" : ""));

        Document document;
        using (RevitApiContext.BeginFailureSuppressionScope())
        using (RevitContext.BeginDialogSuppressionScope(args => OnDialogBoxShowing(args, options.AllowUpgrade)))
        using (new RevitProgressRelay(application, progress, "Открытие модели"))
        {
            document = application.OpenDocumentFile(modelPath, openOptions)
                       ?? throw new InvalidOperationException("Revit не смог открыть модель");
        }

        // Revit не дал прервать открытие и дождался его конца — закрываем уже открытую модель.
        if (progress.ShouldAbort)
        {
            document.Close(false);
            throw new OperationCanceledException();
        }

        var links = new FilteredElementCollector(document).OfClass(typeof(RevitLinkType)).ToElementIds();
        if (links.Count > 0)
            log.Step(job.Name, $"связей загружено: {links.Count(id => RevitLinkType.IsLoaded(document, id))} из {links.Count}");

        return new OpenedModel(document, transmitted, unloaded > 0);
    }

    /// <summary>
    ///     Помечает в копии все связи RVT «не загружать». Revit учитывает это только у файла с пометкой «передан»,
    ///     поэтому копия помечается переданной; при сохранении пометка снимается. Возвращает число связей.
    /// </summary>
    private static int UnloadLinks(string filePath) => Rewrite(filePath, load: false, transmitted: true);

    /// <summary>Снимает пометку «передан» с сохранённого обычного (не совместного) файла — SaveAs этого не умеет.</summary>
    public static void ClearTransmitted(string filePath) => Rewrite(filePath, load: null, transmitted: false);

    private static int Rewrite(string filePath, bool? load, bool? transmitted)
    {
        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
        var data = TransmissionData.ReadTransmissionData(modelPath);
        if (data is null) return 0;

        var count = 0;
        if (load is { } shouldLoad)
        {
            foreach (var id in data.GetAllExternalFileReferenceIds())
            {
                var reference = data.GetLastSavedReferenceData(id);
                if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink) continue;

                data.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, shouldLoad);
                count++;
            }

            if (count == 0) return 0;
        }

        if (transmitted is { } value) data.IsTransmitted = value;
        TransmissionData.WriteTransmissionData(modelPath, data);
        return count;
    }

    private static bool IsTransmitted(ModelPath modelPath)
    {
        try
        {
            return TransmissionData.IsDocumentTransmitted(modelPath);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false; // путь Revit Server — TransmissionData с ним не работает
        }
    }

    private static int? ParseYear(string? format) => int.TryParse(format, out var year) ? year : null;

    private static WorksetConfiguration Worksets(ModelPath modelPath, string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length == 0) return new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets);

        var open = WorksharingUtils.GetUserWorksetInfo(modelPath)
            .Where(workset => workset.Name.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase) < 0)
            .Select(workset => workset.Id)
            .ToList();

        var configuration = new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);
        configuration.Open(open);
        return configuration;
    }

    /// <summary>
    ///     Те же ответы, что в ParamChecker: отсоединить — да, остальное — по умолчанию. Обновить версию — да, только если
    ///     обновление разрешено: проверку версии до открытия Revit может и не пропустить к этому диалогу, это страховка.
    /// </summary>
    private static void OnDialogBoxShowing(DialogBoxShowingEventArgs args, bool allowUpgrade)
    {
        switch (args.DialogId)
        {
            case "Dialog_Revit_DocUpgrade":
                args.OverrideResult((int)(allowUpgrade ? TaskDialogResult.Ok : TaskDialogResult.Cancel));
                break;
            case "TaskDialog_DetachModel":
                args.OverrideResult((int)TaskDialogResult.Yes);
                break;
            default:
                args.OverrideResult(1001);
                break;
        }
    }
}
