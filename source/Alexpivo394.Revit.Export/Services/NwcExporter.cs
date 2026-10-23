using System.IO;
using Alexpivo394.Revit.Export.Core;
using Autodesk.Revit.DB;

namespace Alexpivo394.Revit.Export.Services;

/// <summary>Экспорт одного 3D-вида в NWC через Navisworks Exporter для Revit.</summary>
public static class NwcExporter
{
    public static bool IsAvailable() => OptionalFunctionalityUtils.IsNavisworksExporterAvailable();

    /// <param name="linksUnloaded">Связи RVT не загружались при открытии — их содержимого на виде нет.</param>
    public static StepResult Export(Document document, string folder, string name, ExportOptions options, bool linksUnloaded, out string? note)
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

        // Экспортер сам не всегда перезаписывает файл — удаляем старый; занятый файл даст понятную ошибку.
        var target = Path.Combine(folder, name + ".nwc");
        if (File.Exists(target)) File.Delete(target);

        document.Export(folder, name, Options(view, options));

        // Экспорт ничего не возвращает: без файла выгрузка не удалась, даже если Revit не сообщил об ошибке.
        if (!File.Exists(target) || new FileInfo(target).Length == 0)
            throw new InvalidOperationException("Navisworks Exporter не создал файл NWC");

        note = null;
        return StepResult.Done;
    }

    /// <summary>
    ///     На виде есть что выгружать: видимый элемент модели или, если связи выгружаются в NWC, загруженная связь.
    ///     Сборщик с видом учитывает скрытие на виде, фильтры, рамку и закрытые рабочие наборы.
    /// </summary>
    private static bool HasExportableElements(Document document, View3D view, ExportOptions options, bool linksUnloaded)
    {
        foreach (var element in new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType())
        {
            if (element is RevitLinkInstance link)
            {
                if (options.NwcExportLinks && !linksUnloaded && link.GetLinkDocument() is not null) return true;
                continue;
            }

            if (element.Category is { CategoryType: CategoryType.Model }) return true;
        }

        return false;
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
