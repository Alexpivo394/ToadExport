namespace Alexpivo394.Revit.Export.Core;

/// <summary>Что получилось с одной моделью.</summary>
public sealed class ModelResult
{
    public string Name { get; set; } = "";

    public StepResult Rvt { get; set; }

    public StepResult Nwc { get; set; }

    /// <summary>Причина пропуска или текст ошибки.</summary>
    public string? Note { get; set; }

    public TimeSpan Elapsed { get; set; }

    /// <summary>Нажали «Пропустить модель» или «Прервать».</summary>
    public bool SkippedByUser { get; set; }

    /// <summary>
    ///     Шаг упал на внутренней ошибке Revit (<c>Autodesk.Revit.Exceptions.InternalException</c>, «A managed exception was
    ///     thrown by Revit or by one of its external applications»), а не из-за самой модели. Повтор в новом процессе Revit
    ///     обычно проходит.
    /// </summary>
    public bool RevitFailure { get; set; }

    /// <summary>
    ///     Модель не удалось закрыть: она может остаться открытой и держать память, а состояние Revit неизвестно. Вызывающему,
    ///     который переиспользует процесс Revit (ферма), лучше его перезапустить.
    /// </summary>
    public bool CloseFailed { get; set; }

    public bool HasErrors => Rvt == StepResult.Failed || Nwc == StepResult.Failed;

    public bool IsExported => !SkippedByUser && !HasErrors && (Rvt == StepResult.Done || Nwc == StepResult.Done);

    /// <summary>Ни ошибок, ни пропуска, но и выгружать было нечего — например, нет вида для NWC.</summary>
    public bool IsNothingExported => !SkippedByUser && !HasErrors && !IsExported;
}

public enum StepResult
{
    NotRequested,
    Done,
    Skipped,
    Failed
}
