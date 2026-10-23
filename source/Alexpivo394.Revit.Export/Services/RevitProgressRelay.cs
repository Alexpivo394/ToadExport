using Alexpivo394.Revit.Export.Core;
using Autodesk.Revit.DB.Events;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Alexpivo394.Revit.Export.Services;

/// <summary>
///     Передаёт ход долгой операции Revit (открытие модели, копирование, сохранение) в <see cref="IExportProgress" />.
///     <para>
///         Если выгрузку просят прервать, просит Revit отменить операцию (<see cref="ProgressChangedEventArgs.Cancel" />) —
///         тогда, например, <c>OpenDocumentFile</c> бросает исключение, не дожидаясь конца открытия. Revit разрешает отмену
///         не на каждом шаге, поэтому запрос повторяется при каждом событии, пока операция не прервётся.
///     </para>
/// </summary>
public sealed class RevitProgressRelay : IDisposable
{
    private readonly RevitApplication _application;
    private readonly IExportProgress _progress;
    private readonly string? _prefix;

    /// <param name="application">Приложение Revit, чьи операции отслеживаются.</param>
    /// <param name="progress">Куда сообщать ход.</param>
    /// <param name="prefix">Подпись перед текстом Revit, например «Открытие модели».</param>
    public RevitProgressRelay(RevitApplication application, IExportProgress progress, string? prefix = null)
    {
        _application = application;
        _progress = progress;
        _prefix = prefix;
        _application.ProgressChanged += OnProgressChanged;
    }

    /// <summary>Отмена была запрошена у Revit хотя бы раз.</summary>
    public bool CancelRequested { get; private set; }

    public void Dispose() => _application.ProgressChanged -= OnProgressChanged;

    private void OnProgressChanged(object? sender, ProgressChangedEventArgs e)
    {
        try
        {
            switch (e.Stage)
            {
                case ProgressStage.Started:
                case ProgressStage.CaptionChanged:
                    _progress.Status(Describe(e.Caption));
                    break;
                case ProgressStage.RangeChanged:
                case ProgressStage.PositionChanged:
                    _progress.Report(e.Position, e.UpperRange, Describe(e.Caption));
                    break;
                case ProgressStage.Finished:
                    _progress.Status(_prefix);
                    break;
            }

            if (_progress.ShouldAbort && e.Stage is ProgressStage.PositionChanged or ProgressStage.Unchanged)
            {
                e.Cancel();
                CancelRequested = true;
            }
        }
        catch
        {
            // Эту операцию Revit отменить не даёт — попробуем на следующем событии.
        }
    }

    private string? Describe(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption)) return _prefix;
        return _prefix == null ? caption : $"{_prefix}: {caption}";
    }
}
