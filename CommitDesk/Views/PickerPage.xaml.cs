using CommitDesk.Services;

namespace CommitDesk.Views;

public partial class PickerPage : ContentPage
{
    readonly List<PickerItem> _items;
    // Continuations must not run inside the list's selection/pointer handlers (see Finish).
    readonly TaskCompletionSource<PickerItem?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _finishing;

    public Task<PickerItem?> Result => _result.Task;

    public PickerPage(string title, List<PickerItem> items, string? emptyText)
    {
        InitializeComponent();
        _items = items;
        TitleLabel.Text = title;
        if (emptyText is not null) EmptyLabel.Text = emptyText;
        List.ItemsSource = _items;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Search.Focus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_finishing) _result.TrySetResult(null);   // dismissed with the back button
    }

    void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var q = e.NewTextValue?.Trim();
        List.ItemsSource = string.IsNullOrEmpty(q)
            ? _items
            : _items.Where(i => i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                (i.Subtitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
    }

    void OnSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is PickerItem item) Finish(item);
    }

    void OnCancel(object? sender, EventArgs e) => Finish(null);

    /// <summary>
    /// Closes the page after the current input event has finished, then hands back the result. Popping the
    /// page (and letting the caller rebuild the main window) from inside the list's click handling tears down
    /// UI that WinUI is still dispatching to, which crashes in Microsoft.UI.Xaml.dll (0xc000027b).
    /// </summary>
    void Finish(PickerItem? item)
    {
        if (_finishing) return;
        _finishing = true;
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync();
            }
            finally
            {
                _result.TrySetResult(item);
            }
        });
    }
}
