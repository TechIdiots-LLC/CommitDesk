using CommitDesk.Services;

namespace CommitDesk.Views;

/// <summary>A heading and the picker items under it.</summary>
public sealed class PickerGroup(string name, IEnumerable<PickerItem> items) : List<PickerItem>(items)
{
    public string Name { get; } = name;
}

/// <summary>
/// A searchable list that drops down under a toolbar button (the repository and branch lists)
/// instead of covering the window. Items with a <see cref="PickerItem.Group"/> are shown under headings, and
/// actions (such as "New branch") are buttons beside the filter. A click outside the panel or Esc cancels.
/// An optional second tab (pull/merge requests) loads in the background while the dropdown is open.
/// </summary>
public partial class PickerDropdown : ContentView
{
    sealed class Tab
    {
        public List<PickerItem> Items = [];
        public string EmptyText = "Nothing to show";
    }

    readonly Tab[] _tabs;
    int _active;
    readonly List<View> _actionButtons = [];
    readonly bool _grouped;
    readonly CancellationTokenSource _loading = new();
    // Continuations must not run inside the list's selection/pointer handlers (see Finish).
    readonly TaskCompletionSource<PickerItem?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _finishing;

    public Task<PickerItem?> Result => _result.Task;

    /// <summary>Raised once the dropdown has a result and should be removed from the page.</summary>
    public event EventHandler? Closed;

    public PickerDropdown(IReadOnlyList<PickerItem> items, IReadOnlyList<PickerItem> actions, string? emptyText,
        PickerTabSource? secondTab = null)
    {
        InitializeComponent();
        var first = new Tab { Items = items.ToList() };
        if (emptyText is not null) first.EmptyText = emptyText;
        _tabs = secondTab is null ? [first] : [first, new Tab { EmptyText = secondTab.LoadingText }];
        // Requests are listed under a heading, so a dropdown with them is grouped throughout.
        _grouped = secondTab is not null || first.Items.Any(i => i.Group is not null);
        List.IsGrouped = _grouped;
        ShowTab(0);

        foreach (var action in actions)
        {
            var button = new Button { Text = action.Title, Style = (Style)Application.Current!.Resources["SecondaryButton"], VerticalOptions = LayoutOptions.Center };
            button.Clicked += (_, _) => Finish(action);
            Header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Header.Add(button, Header.ColumnDefinitions.Count - 1, 0);
            _actionButtons.Add(button);
        }

        if (secondTab is not null)
        {
            TabBar.IsVisible = true;
            SecondTabLabel.Text = secondTab.Title;
            _ = LoadAsync(secondTab);
        }

        Loaded += (_, _) => Search.Focus();
        HandlerChanged += (_, _) => HookKeys();
    }

    /// <summary>Places the panel under a toolbar button: from its left edge, below it, down to the window's bottom.</summary>
    public void Place(double x, double top, double width)
    {
        Panel.Margin = new Thickness(x, top, 0, 0);
        Panel.WidthRequest = width;
    }

    void OnFirstTabTapped(object? sender, EventArgs e) => ShowTab(0);
    void OnSecondTabTapped(object? sender, EventArgs e) => ShowTab(1);

    void ShowTab(int index)
    {
        if (index >= _tabs.Length) return;
        _active = index;
        FirstTabLine.Color = index == 0 ? (Color)Application.Current!.Resources["GitLabOrange"] : Colors.Transparent;
        SecondTabLine.Color = index == 1 ? (Color)Application.Current!.Resources["GitLabOrange"] : Colors.Transparent;
        FirstTabLabel.FontAttributes = index == 0 ? FontAttributes.Bold : FontAttributes.None;
        SecondTabLabel.FontAttributes = index == 1 ? FontAttributes.Bold : FontAttributes.None;
        foreach (var b in _actionButtons) b.IsVisible = index == 0;   // e.g. New branch belongs to the branches
        EmptyLabel.Text = _tabs[index].EmptyText;
        Show(Filtered());
    }

    /// <summary>Fills the second tab as its source produces lists; stops when the dropdown closes.</summary>
    async Task LoadAsync(PickerTabSource source)
    {
        var tab = _tabs[1];
        try
        {
            await foreach (var update in source.Load(_loading.Token))
            {
                tab.Items = update.Items.ToList();
                if (update.EmptyText is not null) tab.EmptyText = update.EmptyText;
                if (update.Title is not null) SecondTabLabel.Text = update.Title;
                if (_active == 1) ShowTab(1);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            tab.EmptyText = ex.Message;
            if (_active == 1) ShowTab(1);
        }
    }

    void Show(IEnumerable<PickerItem> items)
    {
        List.ItemsSource = _grouped
            ? items.GroupBy(i => i.Group ?? "").Select(g => new PickerGroup(g.Key, g)).ToList()
            : items.ToList();
    }

    IEnumerable<PickerItem> Filtered()
    {
        var q = Search.Text?.Trim();
        var items = _tabs[_active].Items;
        return string.IsNullOrEmpty(q)
            ? items
            : items.Where(i => i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                (i.Subtitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    void OnSearchChanged(object? sender, TextChangedEventArgs e) => Show(Filtered());

    // Enter picks the first match, so typing part of a name and pressing Enter switches to it.
    void OnSearchSubmitted(object? sender, EventArgs e)
    {
        if (Filtered().FirstOrDefault() is { } first) Finish(first);
    }

    void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is PickerItem item) Finish(item);
    }

    void OnOutsideTapped(object? sender, TappedEventArgs e) => Finish(null);

    public void Cancel() => Finish(null);

    /// <summary>
    /// Closes after the current input event has finished, then hands back the result. Removing the list (and letting
    /// the caller rebuild the main window) from inside its click handling tears down UI that WinUI is still
    /// dispatching to, which crashes in Microsoft.UI.Xaml.dll (0xc000027b).
    /// </summary>
    void Finish(PickerItem? item)
    {
        if (_finishing) return;
        _finishing = true;
        _loading.Cancel();
        Dispatcher.Dispatch(() =>
        {
            try
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
            finally
            {
                _result.TrySetResult(item);
            }
        });
    }

    void HookKeys()
    {
#if WINDOWS
        // The search box swallows Esc, so listen for handled key presses too.
        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement view)
        {
            view.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Escape)
                    {
                        e.Handled = true;
                        Cancel();
                    }
                }), handledEventsToo: true);
        }
#endif
    }
}
