using System.Collections;
using System.Windows.Input;
using CommitDesk.Core.Git;

namespace CommitDesk.Controls;

/// <summary>
/// Unified diff with line numbers. When <see cref="ToggleCommand"/> is set, clicking the gutter selects lines.
/// Pressing on the text and dragging highlights lines to copy, as in GitHub Desktop: right-click › Copy, or Ctrl+C
/// (Windows), copies them without the +/- markers.
/// </summary>
public partial class DiffView : ContentView
{
    public static readonly BindableProperty LinesProperty =
        BindableProperty.Create(nameof(Lines), typeof(IEnumerable), typeof(DiffView),
            propertyChanged: (b, old, lines) =>
            {
                var view = (DiffView)b;
                ClearHighlight(old as IEnumerable);
                view._anchor = null;
                if (lines is IList { Count: > 0 }) view.List.ScrollTo(0, animate: false);
            });

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(DiffView));

    public static readonly BindableProperty ToggleCommandProperty =
        BindableProperty.Create(nameof(ToggleCommand), typeof(ICommand), typeof(DiffView));

    DiffLine? _anchor;
    bool _dragging;

    public DiffView()
    {
        InitializeComponent();
#if WINDOWS
        HandlerChanged += (_, _) => HookCopyShortcut();
#endif
    }

    public IEnumerable? Lines
    {
        get => (IEnumerable?)GetValue(LinesProperty);
        set => SetValue(LinesProperty, value);
    }

    public string? Message
    {
        get => (string?)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    List<DiffLine> AllLines => Lines?.OfType<DiffLine>().ToList() ?? [];

    static void ClearHighlight(IEnumerable? lines)
    {
        foreach (var line in lines?.OfType<DiffLine>() ?? []) line.IsHighlighted = false;
    }

    /// <summary>Highlights the lines from the anchor to <paramref name="to"/>, either way round.</summary>
    void HighlightRange(DiffLine to)
    {
        var lines = AllLines;
        var a = _anchor is null ? -1 : lines.IndexOf(_anchor);
        var b = lines.IndexOf(to);
        if (a < 0) a = b;
        var (from, until) = a <= b ? (a, b) : (b, a);
        for (int i = 0; i < lines.Count; i++) lines[i].IsHighlighted = i >= from && i <= until;
    }

    void OnLinePressed(object? sender, PointerEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not DiffLine line) return;
        // A right-click inside the highlight keeps it for the menu's Copy
        if (e.Button == ButtonsMask.Secondary)
        {
            if (!line.IsHighlighted)
            {
                ClearHighlight(Lines);
                _anchor = line;
                line.IsHighlighted = true;
            }
            return;
        }
        _anchor = line;
        _dragging = true;
        HighlightRange(line);
    }

    void OnLineEntered(object? sender, PointerEventArgs e)
    {
        if (!_dragging || (sender as BindableObject)?.BindingContext is not DiffLine line) return;
#if WINDOWS
        // Released outside the diff: stop extending once the pointer comes back without the button held
        if (e.PlatformArgs?.PointerRoutedEventArgs is { } args && !args.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            return;
        }
#endif
        HighlightRange(line);
    }

    void OnLineReleased(object? sender, PointerEventArgs e) => _dragging = false;

    /// <summary>The highlighted lines' text, top to bottom, without the +/- markers; hunk headers are left out.</summary>
    string? HighlightedText()
    {
        var lines = AllLines.Where(l => l.IsHighlighted && l.Kind != DiffLineKind.Hunk).Select(l => l.Text).ToList();
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    async void OnCopyClicked(object? sender, EventArgs e)
    {
        if (HighlightedText() is { } text) await Clipboard.Default.SetTextAsync(text);
    }

    void OnSelectAllClicked(object? sender, EventArgs e)
    {
        foreach (var line in AllLines) line.IsHighlighted = true;
    }

#if WINDOWS
    Microsoft.UI.Xaml.UIElement? _shortcutRoot;

    /// <summary>
    /// Ctrl+C copies the highlighted lines. The diff's labels never take keyboard focus, so listen on the window's root,
    /// and leave Ctrl+C to a text box that has focus (the commit message, a search box).
    /// </summary>
    void HookCopyShortcut()
    {
        if (_shortcutRoot is not null || Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement view) return;
        view.Loaded += (_, _) =>
        {
            if (_shortcutRoot is not null || view.XamlRoot?.Content is not Microsoft.UI.Xaml.UIElement root) return;
            _shortcutRoot = root;
            root.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(async (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.C || !IsVisible) return;
                var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
                if (!ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot);
                if (focused is Microsoft.UI.Xaml.Controls.TextBox or Microsoft.UI.Xaml.Controls.RichEditBox) return;
                if (HighlightedText() is { } text)
                {
                    args.Handled = true;
                    await Clipboard.Default.SetTextAsync(text);
                }
            }), handledEventsToo: true);
        };
    }
#endif
}
