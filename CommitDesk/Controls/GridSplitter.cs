namespace CommitDesk.Controls;

/// <summary>
/// A vertical divider that resizes the grid column to its left when dragged. Put it in its own narrow column of a
/// Grid; the column to its left needs an absolute width. The width is remembered under <see cref="PreferenceKey"/>.
/// On Windows it captures the mouse (dragging keeps working when the pointer leaves the thin handle) and shows the
/// resize cursor; elsewhere it uses a pan gesture.
/// </summary>
public partial class GridSplitter : ContentView
{
    public static readonly BindableProperty MinimumSizeProperty =
        BindableProperty.Create(nameof(MinimumSize), typeof(double), typeof(GridSplitter), 200.0);

    public static readonly BindableProperty MinimumRemainingProperty =
        BindableProperty.Create(nameof(MinimumRemaining), typeof(double), typeof(GridSplitter), 300.0);

    public static readonly BindableProperty PreferenceKeyProperty =
        BindableProperty.Create(nameof(PreferenceKey), typeof(string), typeof(GridSplitter));

    /// <summary>Smallest width of the column being resized.</summary>
    public double MinimumSize
    {
        get => (double)GetValue(MinimumSizeProperty);
        set => SetValue(MinimumSizeProperty, value);
    }

    /// <summary>Space always left for the columns to the right.</summary>
    public double MinimumRemaining
    {
        get => (double)GetValue(MinimumRemainingProperty);
        set => SetValue(MinimumRemainingProperty, value);
    }

    /// <summary>Preferences key the width is saved under; nothing is saved when null.</summary>
    public string? PreferenceKey
    {
        get => (string?)GetValue(PreferenceKeyProperty);
        set => SetValue(PreferenceKeyProperty, value);
    }

    double _startWidth;

    public GridSplitter()
    {
        WidthRequest = 7;
        BackgroundColor = Colors.Transparent;
        var line = new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.Center };
        line.SetAppThemeColor(BoxView.ColorProperty, Color.FromArgb("#D0D7DE"), Color.FromArgb("#30363D"));
        Content = line;

#if !WINDOWS
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started: BeginDrag(); break;
                case GestureStatus.Running: DragTo(e.TotalX); break;
                case GestureStatus.Completed or GestureStatus.Canceled: EndDrag(); break;
            }
        };
        GestureRecognizers.Add(pan);
#endif
    }

    ColumnDefinition? Column
    {
        get
        {
            if (Parent is not Grid grid) return null;
            int index = Grid.GetColumn(this) - 1;
            return index >= 0 && index < grid.ColumnDefinitions.Count ? grid.ColumnDefinitions[index] : null;
        }
    }

    /// <summary>Applies the saved width, if any. Called when the splitter is added to its grid.</summary>
    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (PreferenceKey is { } key && Column is { } column && Preferences.Get(key, -1.0) is var saved and > 0)
            column.Width = new GridLength(Math.Max(MinimumSize, saved));
    }

    bool _dragging;

    void BeginDrag()
    {
        if (Column is not { } column || Parent is not Grid grid) return;
        _startWidth = column.Width.IsAbsolute ? column.Width.Value : grid.Width / 3;
        _dragging = true;
    }

    void DragTo(double totalX)
    {
        if (!_dragging || Column is not { } column || Parent is not Grid grid) return;
        double max = Math.Max(MinimumSize, grid.Width - MinimumRemaining);
        column.Width = new GridLength(Math.Clamp(_startWidth + totalX, MinimumSize, max));
        // Changing a column's width alone doesn't re-run the grid's layout on Windows; ask for it explicitly.
        ((IView)grid).InvalidateMeasure();
    }

    /// <summary>Saves the width, but only after an actual drag (capture can also be lost without one).</summary>
    void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (PreferenceKey is { } key && Column is { Width.IsAbsolute: true } column)
            Preferences.Set(key, column.Width.Value);
    }

#if WINDOWS
    double _pressX;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element) return;

        // ProtectedCursor is the only way to set a WinUI element's cursor, and it is protected.
        typeof(Microsoft.UI.Xaml.UIElement)
            .GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .SetValue(element, Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast));

        element.PointerPressed += (_, e) =>
        {
            if (!element.CapturePointer(e.Pointer)) return;
            _pressX = e.GetCurrentPoint(null).Position.X;
            BeginDrag();
            e.Handled = true;
        };
        element.PointerMoved += (_, e) =>
        {
            if (element.PointerCaptures?.Count is not > 0) return;
            // Window coordinates are in device-independent pixels, the same unit as MAUI layout.
            DragTo(e.GetCurrentPoint(null).Position.X - _pressX);
            e.Handled = true;
        };
        element.PointerReleased += (_, e) =>
        {
            if (element.PointerCaptures?.Count is not > 0) return;
            element.ReleasePointerCapture(e.Pointer);
            EndDrag();
            e.Handled = true;
        };
        element.PointerCaptureLost += (_, _) => EndDrag();
    }
#endif
}
