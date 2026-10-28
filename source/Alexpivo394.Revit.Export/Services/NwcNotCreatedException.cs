namespace Alexpivo394.Revit.Export.Services;

/// <summary>На виде NWC есть элементы, но Navisworks Exporter не создал файл.</summary>
public sealed class NwcNotCreatedException(string message) : InvalidOperationException(message);
