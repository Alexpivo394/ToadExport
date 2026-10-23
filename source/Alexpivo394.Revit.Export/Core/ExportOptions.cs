namespace Alexpivo394.Revit.Export.Core;

/// <summary>
///     Как открыть модель и что с ней сделать при выгрузке. Значения NWC по умолчанию — как у
///     <c>new NavisworksExportOptions()</c>, кроме выгрузки одного вида: она задаётся именем вида.
///     Откуда брать модели и куда класть результат, в опции не входит — это параметры запуска.
/// </summary>
public sealed record ExportOptions
{
    // Открытие
    public bool SkipLinks { get; init; } = true;
    public bool Audit { get; init; }
    public string WorksetKeyword { get; init; } = "";

    /// <summary>
    ///     Можно ли открыть модель более старой версии — Revit обновит её в памяти. При <c>false</c> такая модель
    ///     не открывается: <see cref="VersionGuard" />.
    /// </summary>
    public bool AllowUpgrade { get; init; } = true;

    // RVT — обработка перед сохранением
    public bool DeleteRevitLinks { get; init; } = true;
    public bool DeleteCad { get; init; } = true;
    public bool DeletePointClouds { get; init; }
    public bool Purge { get; init; } = true;
    public bool Compact { get; init; } = true;

    // NWC — основное
    public string NwcViewName { get; init; } = "Navisworks";
    public bool NwcSharedCoordinates { get; init; } = true;
    public bool NwcConvertElementProperties { get; init; }
    public NwcParameters NwcParameters { get; init; } = NwcParameters.All;
    public bool NwcDivideIntoLevels { get; init; } = true;

    // NWC — что экспортировать
    public bool NwcExportLinks { get; init; }
    public bool NwcConvertLinkedCad { get; init; } = true;
    public bool NwcExportParts { get; init; }
    public bool NwcRoomGeometry { get; init; } = true;
    public bool NwcRoomAsAttribute { get; init; } = true;
    public bool NwcConvertLights { get; init; }

    // NWC — дополнительно
    public bool NwcExportElementIds { get; init; } = true;
    public bool NwcExportUrls { get; init; } = true;
    public bool NwcFindMissingMaterials { get; init; } = true;
    public double NwcFacetingFactor { get; init; } = 1.0;
}

/// <summary>Какие параметры элементов попадают в NWC (как <c>Autodesk.Revit.DB.NavisworksParameters</c>).</summary>
public enum NwcParameters
{
    None,
    Elements,
    All
}
