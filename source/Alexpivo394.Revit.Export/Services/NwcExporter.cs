using System.IO;
using Alexpivo394.Revit.Export.Core;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Nice3point.Revit.Toolkit;

namespace Alexpivo394.Revit.Export.Services;

/// <summary>Экспорт одного 3D-вида в NWC через Navisworks Exporter для Revit.</summary>
public static class NwcExporter
{
    public static bool IsAvailable() => OptionalFunctionalityUtils.IsNavisworksExporterAvailable();

    /// <param name="linksUnloaded">Связи RVT не загружались при открытии — их содержимого на виде нет.</param>
    /// <param name="progress">Куда передавать ход экспорта, если Revit о нём сообщает, и откуда узнать, что экспорт просят прервать.</param>
    public static StepResult Export(Document document, string folder, string name, ExportOptions options, bool linksUnloaded, IExportProgress progress,
        out string? note) => Export(document, folder, name, options, linksUnloaded, progress, out note, out _);

    /// <param name="missing">
    ///     NWC не получился, хотя вид есть, и пустоту вида не объясняют невыгружаемые связи (<see cref="ModelResult.NwcMissing" />).
    /// </param>
    /// <exception cref="NwcNotCreatedException">На виде есть элементы, но Navisworks Exporter не создал файл.</exception>
    public static StepResult Export(Document document, string folder, string name, ExportOptions options, bool linksUnloaded, IExportProgress progress,
        out string? note, out bool missing)
    {
        missing = false;
        var viewName = options.NwcViewName.Trim();
        var view = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate && string.Equals(view.Name, viewName, StringComparison.CurrentCultureIgnoreCase));

        if (view is null)
        {
            note = $"вид «{viewName}» не найден, NWC пропущен";
            return StepResult.Skipped;
        }

        // Проверка вида только объясняет, почему файла нет, и не заменяет экспорт: сборщик по виду в фоновом документе
        // бывает пуст на виде, где Navisworks Exporter элементы находит.
        var content = Inspect(document, view, options, linksUnloaded);

        // Экспортер сам не всегда перезаписывает файл — удаляем старый. ModelExporter экспортирует в пустую временную папку
        // и подменяет результат готовым файлом (OutputFile), так что прошлая выгрузка здесь не теряется.
        var target = Path.Combine(folder, name + ".nwc");
        if (File.Exists(target)) File.Delete(target);

        // Окно экспортера (например, «Не найдена подходящая геометрия…») в пакетной выгрузке закрыть некому: поток Revit API
        // ждал бы его до таймаута. Отвечаем «ОК» и запоминаем текст — он объясняет, почему файла нет.
        var dialogs = new List<string>();
        // NavisworksExportOptions держит неуправляемый объект Revit — освобождаем сразу, а не когда дойдёт сборщик мусора.
        using (var exportOptions = Options(view, options))
        using (RevitContext.BeginDialogSuppressionScope(args => OnDialogBoxShowing(args, dialogs)))
        using (new RevitProgressRelay(document.Application, progress, "Экспорт NWC"))
        {
            try
            {
                document.Export(folder, name, exportOptions);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException exception) when (content != ViewContent.Exportable &&
                                                                                    exception is not Autodesk.Revit.Exceptions.InternalException)
            {
                // Пустой вид экспортер отвергает ошибкой «нет подходящих элементов».
                dialogs.Add($"Revit сообщил: «{exception.Message.Trim()}»");
            }
        }

        // Экспорт ничего не возвращает: без файла выгрузка не удалась, даже если Revit не сообщил об ошибке.
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            note = null;
            return StepResult.Done;
        }

        var reported = dialogs.Count == 0 ? "" : $": {string.Join("; ", dialogs)}";
        if (content == ViewContent.Exportable) throw new NwcNotCreatedException($"Navisworks Exporter не создал файл NWC{reported}");

        missing = content == ViewContent.Nothing;
        note = $"на виде «{viewName}» нет видимых элементов модели{EmptyViewHint(options, linksUnloaded)}, NWC пропущен{reported}";
        return StepResult.Skipped;
    }

    /// <summary>Что на виде нашёл сборщик по виду.</summary>
    private enum ViewContent
    {
        /// <summary>Видимый элемент модели с объёмной геометрией или выгружаемая связь.</summary>
        Exportable,

        /// <summary>Только связи, которые в NWC не попадут: не загружались или их экспорт выключен.</summary>
        LinksOnly,

        /// <summary>Ни одного элемента модели с объёмной геометрией.</summary>
        Nothing
    }

    /// <summary>
    ///     Есть ли на виде что выгружать: видимый элемент модели с объёмной геометрией или, если связи выгружаются в NWC,
    ///     загруженная связь. Сборщик с видом учитывает скрытие на виде, фильтры и закрытые рабочие наборы. Одной категории
    ///     модели мало: помещения, пространства, системы и линии модели тоже попадают в сборщик, но Navisworks Exporter
    ///     геометрией их не считает и отвечает «Не найдена подходящая геометрия».
    /// </summary>
    private static ViewContent Inspect(Document document, View3D view, ExportOptions options, bool linksUnloaded)
    {
        var geometryOptions = new Autodesk.Revit.DB.Options { View = view };
        var links = false;
        var modelElements = false;
        foreach (var element in new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType())
        {
            if (element is RevitLinkInstance link)
            {
                if (options.NwcExportLinks && !linksUnloaded && link.GetLinkDocument() is not null) return ViewContent.Exportable;
                links = true;
                continue;
            }

            if (element.Category is not { CategoryType: CategoryType.Model }) continue;
            modelElements = true;
            if (element is PointCloudInstance || HasVolume(element.get_Geometry(geometryOptions))) return ViewContent.Exportable;
        }

        return links && !modelElements ? ViewContent.LinksOnly : ViewContent.Nothing;
    }

    private static bool HasVolume(GeometryElement? geometry)
    {
        if (geometry is null) return false;
        foreach (var item in geometry)
        {
            switch (item)
            {
                case Solid solid when solid.Faces.Size > 0:
                case Mesh { NumTriangles: > 0 }:
                    return true;
                case GeometryInstance instance when HasVolume(instance.GetInstanceGeometry()):
                    return true;
            }
        }

        return false;
    }

    /// <summary>Любое окно Revit во время экспорта — сообщение экспортера: «ОК» и текст в итог шага.</summary>
    private static void OnDialogBoxShowing(DialogBoxShowingEventArgs args, List<string> dialogs)
    {
        var message = args switch
        {
            TaskDialogShowingEventArgs task => task.Message,
            MessageBoxShowingEventArgs box => box.Message,
            _ => null
        };
        dialogs.Add(string.IsNullOrWhiteSpace(message) ? $"Revit показал окно {args.DialogId}" : $"Revit сообщил: «{message!.Trim()}»");
        args.OverrideResult((int)TaskDialogResult.Ok);
    }

    private static string EmptyViewHint(ExportOptions options, bool linksUnloaded)
    {
        var reasons = new List<string>();
        if (linksUnloaded) reasons.Add("связи не загружались");
        if (!string.IsNullOrWhiteSpace(options.WorksetKeyword)) reasons.Add($"рабочие наборы с «{options.WorksetKeyword.Trim()}» закрыты");
        return reasons.Count == 0 ? "" : $" ({string.Join(", ", reasons)})";
    }

    private static NavisworksExportOptions Options(View3D view, ExportOptions options) => new()
    {
        ExportScope = NavisworksExportScope.View,
        ViewId = view.Id,
        Coordinates = options.NwcSharedCoordinates ? NavisworksCoordinates.Shared : NavisworksCoordinates.Internal,
        ConvertElementProperties = options.NwcConvertElementProperties,
        Parameters = options.NwcParameters switch
        {
            NwcParameters.None => NavisworksParameters.None,
            NwcParameters.Elements => NavisworksParameters.Elements,
            _ => NavisworksParameters.All
        },
        DivideFileIntoLevels = options.NwcDivideIntoLevels,
        ExportLinks = options.NwcExportLinks,
        ConvertLinkedCADFormats = options.NwcConvertLinkedCad,
        ExportParts = options.NwcExportParts,
        ExportRoomGeometry = options.NwcRoomGeometry,
        ExportRoomAsAttribute = options.NwcRoomAsAttribute,
        ConvertLights = options.NwcConvertLights,
        ExportElementIds = options.NwcExportElementIds,
        ExportUrls = options.NwcExportUrls,
        FindMissingMaterials = options.NwcFindMissingMaterials,
        FacetingFactor = options.NwcFacetingFactor
    };
}
