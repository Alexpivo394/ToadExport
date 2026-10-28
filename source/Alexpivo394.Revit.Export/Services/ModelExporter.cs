using System.Diagnostics;
using System.IO;
using Alexpivo394.Revit.Export.Core;
using Autodesk.Revit.DB;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Alexpivo394.Revit.Export.Services;

/// <summary>
///     Выгружает одну модель: открыть → NWC → RVT → закрыть. Ошибка одного формата не мешает другому; просьба прервать
///     (<see cref="IExportProgress.ShouldAbort" />) закрывает модель без сохранения. Очередь моделей, окно прогресса и
///     итоговый журнал — забота вызывающего.
/// </summary>
public sealed class ModelExporter(RevitApplication application)
{
    private readonly ModelOpener _opener = new(application);

    public ModelResult Export(ModelJob job, ExportOptions options, string outputFolder, IExportProgress progress, IExportLog log)
    {
        var result = new ModelResult { Name = job.Name };
        var notes = new List<string>();
        var stopwatch = Stopwatch.StartNew();
        var output = Path.GetFullPath(outputFolder.Trim());
        var baseName = Path.GetFileNameWithoutExtension(job.Name);
        var target = Path.Combine(output, job.Name);
        Document? document = null;
        OpenedModel? opened = null;
        string? central = null;

        using var temp = TempWorkspace.Create();
        try
        {
            Directory.CreateDirectory(output);
            opened = _opener.Open(job, options, temp, progress, log);
            document = opened.Document;

            if (job.Nwc && !NwcExporter.IsAvailable())
            {
                result.Nwc = StepResult.Skipped;
                notes.Add("Navisworks Exporter не установлен");
            }
            else if (job.Nwc)
            {
                ThrowIfAborted(progress);
                progress.Status("Экспорт NWC");
                log.Step(job.Name, "экспорт NWC");
                result.Nwc = Attempt(progress, log, job, result, notes, "NWC", () =>
                {
                    // Экспорт — во временную папку: зависший или упавший экспорт не трогает прошлый NWC, подменяем только готовым.
                    var staging = Directory.CreateDirectory(Path.Combine(temp.Directory, "nwc")).FullName;
                    var step = NwcExporter.Export(document, staging, baseName, options, opened.LinksUnloaded, progress, out var note, out var missing);
                    Note(notes, note);
                    result.NwcMissing = missing;
                    if (step == StepResult.Done)
                    {
                        progress.Status("Публикация NWC");
                        var bytes = OutputFile.Publish(Path.Combine(staging, baseName + ".nwc"), Path.Combine(output, baseName + ".nwc"));
                        log.Step(job.Name, $"NWC записан, {bytes / (1024.0 * 1024.0):0.#} МБ");
                    }
                    if (step == StepResult.Done && options.NwcExportLinks && opened.LinksUnloaded)
                        notes.Add("связи не загружались — в NWC их нет");
                    return step;
                });
            }

            if (job.Rvt)
            {
                ThrowIfAborted(progress);
                log.Step(job.Name, "подготовка и сохранение RVT");
                result.Rvt = Attempt(progress, log, job, result, notes, "RVT", () =>
                {
                    var path = target;
                    if (document.IsWorkshared)
                        path = Path.Combine(Directory.CreateDirectory(Path.Combine(temp.Directory, "central")).FullName, job.Name);
                    Note(notes, RvtSaver.Save(document, path, opened.IsTransmitted, options, progress));
                    if (document.IsWorkshared) central = path;
                    return StepResult.Done;
                });
            }
        }
        catch (Exception exception) when (progress.ShouldAbort || exception is OperationCanceledException)
        {
            result.SkippedByUser = true;
        }
        catch (Exception exception)
        {
            // Модель не открылась — всё, что просили, не выгружено.
            if (job.Rvt && result.Rvt == StepResult.NotRequested) result.Rvt = StepResult.Failed;
            if (job.Nwc && result.Nwc == StepResult.NotRequested) result.Nwc = StepResult.Failed;
            notes.Add(exception.Message);
            log.Step(job.Name, $"ошибка: {exception}");
            result.RevitFailure |= exception is Autodesk.Revit.Exceptions.InternalException;
        }
        finally
        {
            var workshared = document is { IsValidObject: true, IsWorkshared: true };
            result.CloseFailed = !Close(document, job, progress, log);
            if (result.Rvt == StepResult.Done && central is not null) CreateLocal(central, target, result, notes, progress);
            if (result.Rvt == StepResult.Done && opened is not null)
            {
                // Связи не загружались — Revit сохраняет их выгруженными, у получателя их нужно загрузить вручную.
                if (opened.LinksUnloaded && !options.DeleteRevitLinks) notes.Add("связи RVT сохранены выгруженными");

                // Модели совместной работы пометку «передан» снял SaveAs; обычному файлу её снимаем сами.
                if (opened.IsTransmitted && !workshared) ClearTransmitted(target, notes);
            }
            result.Elapsed = stopwatch.Elapsed;
            result.Note = notes.Count == 0 ? null : string.Join("; ", notes);
        }

        return result;
    }

    private static void CreateLocal(string central, string target, ModelResult result, List<string> notes, IExportProgress progress)
    {
        progress.Status("Создание локальной копии RVT");
        try
        {
            RvtSaver.CreateLocal(central, target);
        }
        catch (Exception exception)
        {
            result.Rvt = StepResult.Failed;
            notes.Add($"RVT: {exception.Message}");
        }
    }

    private static void ClearTransmitted(string filePath, List<string> notes)
    {
        try
        {
            ModelOpener.ClearTransmitted(filePath);
        }
        catch (Exception exception)
        {
            notes.Add($"RVT: не удалось снять пометку «передан»: {exception.Message}");
        }
    }

    /// <summary>Шаг, ошибка которого не мешает следующему шагу той же модели.</summary>
    private static StepResult Attempt(IExportProgress progress, IExportLog log, ModelJob job, ModelResult result, List<string> notes, string format,
        Func<StepResult> step)
    {
        try
        {
            return step();
        }
        catch (Exception exception) when (!progress.ShouldAbort && exception is not OperationCanceledException)
        {
            notes.Add($"{format}: {exception.Message}");
            log.Step(job.Name, $"{format}: {exception}");
            result.RevitFailure |= exception is Autodesk.Revit.Exceptions.InternalException;
            result.NwcMissing |= exception is NwcNotCreatedException;
            return StepResult.Failed;
        }
    }

    private static void Note(List<string> notes, string? note)
    {
        if (!string.IsNullOrEmpty(note)) notes.Add(note!);
    }

    private static void ThrowIfAborted(IExportProgress progress)
    {
        if (progress.ShouldAbort) throw new OperationCanceledException();
    }

    /// <returns>false — модель не закрылась.</returns>
    private static bool Close(Document? document, ModelJob job, IExportProgress progress, IExportLog log)
    {
        if (document is null || !document.IsValidObject) return true;

        progress.Status("Закрытие модели");
        try
        {
            document.Close(false);
            return true;
        }
        catch (Exception exception)
        {
            log.Step(job.Name, $"не удалось закрыть модель: {exception}");
            return false;
        }
    }
}
