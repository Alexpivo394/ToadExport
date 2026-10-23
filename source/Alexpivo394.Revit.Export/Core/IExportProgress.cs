namespace Alexpivo394.Revit.Export.Core;

/// <summary>Куда выгрузка сообщает о ходе работы с моделью и откуда узнаёт, что её просят прервать.</summary>
public interface IExportProgress
{
    /// <summary>Текущий шаг, например «Открытие модели»; null — без подписи.</summary>
    void Status(string? status);

    /// <summary>Ход долгой операции Revit внутри шага.</summary>
    void Report(int current, int total, string? status);

    /// <summary>Модель просят бросить: пропустить, прервать выгрузку или отменить задание.</summary>
    bool ShouldAbort { get; }
}
