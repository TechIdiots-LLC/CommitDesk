using CommitDesk.Views;

namespace CommitDesk.Services;

/// <param name="BadgeColor">Colours the badge (e.g. a CI status glyph); otherwise it is secondary text.</param>
public sealed record PickerItem(string Title, string? Subtitle, object Value, string? Badge = null, string? Group = null,
    Color? BadgeColor = null)
{
    public bool HasPlainBadge => !string.IsNullOrEmpty(Badge) && BadgeColor is null;
    public bool HasColoredBadge => !string.IsNullOrEmpty(Badge) && BadgeColor is not null;
}

/// <summary>One state of a dropdown tab that loads in the background: its items and, optionally, a new title.</summary>
public sealed record PickerTabUpdate(IReadOnlyList<PickerItem> Items, string? Title = null, string? EmptyText = null);

/// <summary>
/// A second dropdown tab (e.g. "Pull requests") filled in the background: each update replaces the list, so a
/// first list can appear quickly and details (CI status) follow. Loading starts when the dropdown opens and stops
/// when it closes.
/// </summary>
public sealed record PickerTabSource(string Title, string LoadingText, Func<CancellationToken, IAsyncEnumerable<PickerTabUpdate>> Load);

/// <summary>The toolbar buttons a picker can drop down from.</summary>
public enum DropdownAnchor { Repository, Branch }

/// <summary>The window that shows pickers as dropdowns (the main page).</summary>
public interface IDropdownHost
{
    Task<PickerItem?> ShowDropdownAsync(DropdownAnchor anchor, IReadOnlyList<PickerItem> items, IReadOnlyList<PickerItem> actions,
        string? emptyText, PickerTabSource? secondTab);
}

/// <summary>Alerts, prompts and modal pages shown on top of whatever is currently displayed.</summary>
public sealed class DialogService
{
    static Page Top
    {
        get
        {
            var root = Application.Current!.Windows[0].Page!;
            return root.Navigation.ModalStack.LastOrDefault() ?? root;
        }
    }

    public Task AlertAsync(string title, string message)
        => MainThread.InvokeOnMainThreadAsync(() => Top.DisplayAlertAsync(title, message, "OK"));

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
        => MainThread.InvokeOnMainThreadAsync(() => Top.DisplayAlertAsync(title, message, accept, cancel));

    public Task<string?> PromptAsync(string title, string message, string? initial = null, string accept = "OK", string? placeholder = null)
        => MainThread.InvokeOnMainThreadAsync(() =>
            Top.DisplayPromptAsync(title, message, accept, "Cancel", placeholder, initialValue: initial ?? ""));

    /// <returns>The chosen button text, or null when cancelled.</returns>
    public async Task<string?> ChooseAsync(string title, params string[] buttons)
    {
        var r = await MainThread.InvokeOnMainThreadAsync(() => Top.DisplayActionSheetAsync(title, "Cancel", null, buttons));
        return r is null or "Cancel" ? null : r;
    }

    /// <summary>Searchable list; returns the chosen item's value, or null when cancelled.</summary>
    public async Task<T?> PickAsync<T>(string title, IEnumerable<PickerItem> items, string? emptyText = null) where T : class
    {
        var page = new PickerPage(title, items.ToList(), emptyText);
        await Top.Navigation.PushModalAsync(page);
        var result = await page.Result;
        return result?.Value as T;
    }

    /// <summary>Set by the main page once it exists.</summary>
    public IDropdownHost? DropdownHost { get; set; }

    /// <summary>
    /// Searchable list that drops down under a toolbar button, with <paramref name="actions"/> as buttons beside the
    /// filter. While a dialog is open it is shown as a picker page instead, with the actions at the top of the list.
    /// </summary>
    public async Task<T?> DropdownAsync<T>(DropdownAnchor anchor, string title, IEnumerable<PickerItem> items,
        IEnumerable<PickerItem>? actions = null, string? emptyText = null, PickerTabSource? secondTab = null) where T : class
    {
        var list = items.ToList();
        var buttons = actions?.ToList() ?? [];
        if (DropdownHost is { } host && Application.Current!.Windows[0].Page!.Navigation.ModalStack.Count == 0)
            return (await MainThread.InvokeOnMainThreadAsync(() => host.ShowDropdownAsync(anchor, list, buttons, emptyText, secondTab)))?.Value as T;
        return await PickAsync<T>(title, buttons.Select(a => a with { Title = $"+ {a.Title}" }).Concat(list.Select(i => i with { Group = null })), emptyText);
    }

    public Task PushModalAsync(Page page) => Top.Navigation.PushModalAsync(page);

    public Task PopModalAsync() => Top.Navigation.PopModalAsync();
}
