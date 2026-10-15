using System.Collections;
using System.Windows.Input;

namespace CommitDesk.Controls;

/// <summary>Unified diff with line numbers. When <see cref="ToggleCommand"/> is set, clicking the gutter selects lines.</summary>
public partial class DiffView : ContentView
{
    public static readonly BindableProperty LinesProperty =
        BindableProperty.Create(nameof(Lines), typeof(IEnumerable), typeof(DiffView),
            propertyChanged: (b, _, lines) =>
            {
                if (lines is IList { Count: > 0 }) ((DiffView)b).List.ScrollTo(0, animate: false);
            });

    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(DiffView));

    public static readonly BindableProperty ToggleCommandProperty =
        BindableProperty.Create(nameof(ToggleCommand), typeof(ICommand), typeof(DiffView));

    public DiffView()
    {
        InitializeComponent();
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
}
