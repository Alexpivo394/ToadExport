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
        out string? note)
    {
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

        // Пустой вид Navisworks Exporter либо отвергает ошибкой «нет подходящих элементов», либо молча не создаёт файл.
        if (!HasExportableElements(document, view, options, linksUnloaded))
        {
            note = $"на виде «{viewName}» нет видимых элементов модели{EmptyViewHint(options, linksUnloaded)}, NWC пропущен";
            return StepResult.Skipped;
        }

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
            document.Export(folder, name, exportOptions);
        }

        // Экспорт ничего не возвращает: без файла выгрузка не удалась, даже если Revit не сообщил об ошибке.
        if (!File.Exists(target) || new FileInfo(target).Length == 0)
            throw new InvalidOperationException("Navisworks Exporter не создал файл NWC" +
                                                (dialogs.Count == 0 ? "" : $": {string.Join("; ", dialogs)}"));

        note = null;
        return StepResult.Done;
    }

    /// <summary>
    ///     На виде есть что выгружать: видимый элемент модели с объёмной геометрией или, если связи выгружаются в NWC,
    ///     загруженная связь. Сборщик с видом учитывает скрытие на виде, фильтры и закрытые рабочие наборы. Одной категории
    ///     модели мало: помещения, пространства, системы и линии модели тоже попадают в сборщик, но Navisworks Exporter
    ///     геометрией их не считает и отвечает «Не найдена подходящая геометрия».
    /// </summary>
    private static bool HasExportableElements(Document document, View3D view, ExportOptions options, bool linksUnloaded)
    {
        var geometryOptions = new Autodesk.Revit.DB.Options { View = view };
        foreach (var element in new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType())
        {
            if (element is RevitLinkInstance link)
            {
                if (options.NwcExportLinks && !linksUnloaded && link.GetLinkDocument() is not null) return true;
                continue;
            }

            if (element.Category is not { CategoryType: CategoryType.Model }) continue;
            if (element is PointCloudInstance || HasVolume(element.get_Geometry(geometryOptions))) return true;
        }

        return false;
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
