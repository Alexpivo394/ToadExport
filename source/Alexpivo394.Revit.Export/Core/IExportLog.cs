namespace Alexpivo394.Revit.Export.Core;

/// <summary>Журнал выгрузки: файл у ручной выгрузки, поток событий агенту ToadFlow.</summary>
public interface IExportLog
{
    void Step(string model, string text);
}
