using Avalonia;
using Avalonia.Controls.Presenters;

namespace BorgMate.Presentation.Controls;

public class SelectionTextPresenter : TextPresenter
{
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Avalonia 12.0.4-12.0.5 keeps cached text runs on selection changes,
        // which can leave selected text painted with the normal foreground.
        if (change.Property == SelectionForegroundBrushProperty ||
            SelectionForegroundBrush is not null &&
            (change.Property == SelectionStartProperty ||
             change.Property == SelectionEndProperty ||
             change.Property == ShowSelectionHighlightProperty))
        {
            InvalidateTextLayout();
        }
    }
}
