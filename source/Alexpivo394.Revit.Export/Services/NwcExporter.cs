using System.IO;
using Alexpivo394.Revit.Export.Core;
using Autodesk.Revit.DB;

namespace Alexpivo394.Revit.Export.Services;

/// <summary>Экспорт одного 3D-вида в NWC через Navisworks Exporter для Revit.</summary>
public static class NwcExporter
{
    public static bool IsAvailable() => OptionalFunctionalityUtils.IsNavisworksExporterAvailable();

    public static StepResult Export(Document document, string folder, string name, ExportOptions options, out string? note)
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

        // Экспортер сам не всегда перезаписывает файл — удаляем старый; занятый файл даст понятную ошибку.
        var target = Path.Combine(folder, name + ".nwc");
        if (File.Exists(target)) File.Delete(target);

        document.Export(folder, name, Options(view, options));
        note = null;
        return StepResult.Done;
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
