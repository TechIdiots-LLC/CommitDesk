using System.Reflection;
using CommitDesk.Core.Git;
using CommitDesk.Services;
using CommitDesk.ViewModels;

namespace CommitDesk.Views;

public partial class MainPage : ContentPage, IDropdownHost
{
    readonly MainViewModel _vm;

    readonly AppUpdater _updater;

    public MainPage(MainViewModel vm, AppUpdater updater, DialogService dialogs)
    {
        _vm = vm;
        CreateTouchCommands();   // before InitializeComponent: the item templates' touch bindings read them
        InitializeComponent();
        BindingContext = vm;
        _updater = updater;
        dialogs.DropdownHost = this;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsDiffExpanded)) ApplyDiffExpanded(vm.IsDiffExpanded);
            OnCompactStateChanged(e.PropertyName);
        };
        Loaded += OnFirstLoaded;
    }

    // Offer a newer release at startup; quietly does nothing when offline or turned off in Options.
    async void OnFirstLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnFirstLoaded;
        await _updater.CheckOnStartupAsync();
    }

    async void OnCheckForUpdatesClicked(object? sender, EventArgs e) => await _updater.CheckAsync(interactive: true);

    async void OnDocumentationClicked(object? sender, EventArgs e) => await Launcher.Default.OpenAsync(new Uri(AppUpdater.DocumentationUrl));

    /// <summary>Shows a picker under the repository or branch button.</summary>
    public Task<PickerItem?> ShowDropdownAsync(DropdownAnchor anchor, IReadOnlyList<PickerItem> items, IReadOnlyList<PickerItem> actions,
        string? emptyText, PickerTabSource? secondTab)
    {
        (DropdownLayer.Content as PickerDropdown)?.Cancel();

        var button = anchor == DropdownAnchor.Repository ? RepositoryButton : BranchButton;
        var x = button.X;
        var width = Math.Min(Math.Max(button.Width, 440), Math.Max(button.Width, Width - x));
        if (_vm.IsCompact) (x, width) = (0, Width);   // a phone: the whole width
        var dropdown = new PickerDropdown(items, actions, emptyText, secondTab);
        dropdown.Place(x, Toolbar.Height, width);
        dropdown.Closed += (_, _) =>
        {
            if (DropdownLayer.Content != dropdown) return;
            DropdownLayer.Content = null;
            DropdownLayer.IsVisible = false;
        };
        DropdownLayer.Content = dropdown;
        DropdownLayer.IsVisible = true;
        return dropdown.Result;
    }

    // Widths of the list columns while the diff is expanded, to put back afterwards.
    GridLength _leftWidth, _historyFilesWidth;

    /// <summary>
    /// Collapses (or restores) the list columns. Hiding a pane leaves its column's width behind, so the columns
    /// themselves go to zero; the splitters keep whatever width the user dragged them to.
    /// </summary>
    void ApplyDiffExpanded(bool expanded)
    {
        var left = RepoGrid.ColumnDefinitions[0];
        var files = HistoryFilesGrid.ColumnDefinitions[0];
        if (expanded)
        {
            _leftWidth = left.Width;
            _historyFilesWidth = files.Width;
            left.Width = new GridLength(0);
            files.Width = new GridLength(0);
        }
        else
        {
            left.Width = _leftWidth;
            files.Width = _historyFilesWidth;
        }
        // A column width change alone doesn't re-run the grid's layout on Windows.
        ((IView)RepoGrid).InvalidateMeasure();
        ((IView)HistoryFilesGrid).InvalidateMeasure();
    }

    // Context menu items inherit the row's BindingContext.
    static T? Item<T>(object? sender) where T : class => (sender as Element)?.BindingContext as T;

    void OnFile(object? sender, Func<ChangedFileViewModel, Task> action)
    {
        if (Item<ChangedFileViewModel>(sender) is { } f) _ = action(f);
    }

    void OnCommit(object? sender, Func<CommitInfo, Task> action)
    {
        if (Item<CommitInfo>(sender) is { } c) _ = action(c);
    }

    // ── Changed file context menu ────────────────────────────────────────────
    void OnDiscardFile(object? s, EventArgs e) => OnFile(s, _vm.DiscardFileAsync);
    void OnIgnoreFile(object? s, EventArgs e) => OnFile(s, _vm.IgnoreFileAsync);
    void OnIgnoreExtension(object? s, EventArgs e) => OnFile(s, _vm.IgnoreExtensionAsync);
    void OnCopyFullPath(object? s, EventArgs e) => OnFile(s, f => _vm.CopyFilePathAsync(f, relative: false));
    void OnCopyRelativePath(object? s, EventArgs e) => OnFile(s, f => _vm.CopyFilePathAsync(f, relative: true));
    void OnCopyFileDiff(object? s, EventArgs e) => OnFile(s, _vm.CopyFileDiffAsync);
    void OnShowFileInFolder(object? s, EventArgs e) => OnFile(s, _vm.ShowFileInFolderAsync);
    void OnOpenFileInEditor(object? s, EventArgs e) => OnFile(s, _vm.OpenFileInEditorAsync);
    void OnOpenFileDefault(object? s, EventArgs e) => OnFile(s, _vm.OpenFileWithDefaultAppAsync);

    // ── Commit context menu ──────────────────────────────────────────────────
    void OnAmendCommit(object? s, EventArgs e) => OnCommit(s, _vm.AmendCommitAsync);
    void OnResetToCommit(object? s, EventArgs e) => OnCommit(s, _vm.ResetToCommitAsync);
    void OnCheckoutCommit(object? s, EventArgs e) => OnCommit(s, _vm.CheckoutCommitAsync);
    void OnRevertCommit(object? s, EventArgs e) => OnCommit(s, _vm.RevertCommitAsync);
    void OnCreateBranchFromCommit(object? s, EventArgs e) => OnCommit(s, _vm.CreateBranchFromCommitAsync);
    void OnCreateTag(object? s, EventArgs e) => OnCommit(s, _vm.CreateTagAsync);
    void OnCherryPick(object? s, EventArgs e) => OnCommit(s, _vm.CherryPickCommitAsync);
    void OnCopySha(object? s, EventArgs e) => OnCommit(s, _vm.CopyShaAsync);
    void OnCopyCommitPatch(object? s, EventArgs e) => OnCommit(s, _vm.CopyCommitPatchAsync);
    void OnCopyTag(object? s, EventArgs e) => OnCommit(s, _vm.CopyTagAsync);
    void OnViewCommitOnHost(object? s, EventArgs e) => OnCommit(s, _vm.ViewCommitOnHostAsync);

    // ── A file in the selected commit ──────────────────────────────────────────
    void OnCommitFile(object? sender, Func<FileChange, Task> action)
    {
        if (Item<FileChange>(sender) is { } f) _ = action(f);
    }

    void OnShowCommitFileInFolder(object? s, EventArgs e) => OnCommitFile(s, _vm.ShowCommitFileInFolderAsync);
    void OnOpenCommitFileInEditor(object? s, EventArgs e) => OnCommitFile(s, _vm.OpenCommitFileInEditorAsync);
    void OnOpenCommitFileDefault(object? s, EventArgs e) => OnCommitFile(s, _vm.OpenCommitFileWithDefaultAppAsync);
    void OnCopyCommitFileDiff(object? s, EventArgs e) => OnCommitFile(s, _vm.CopyCommitFileDiffAsync);
    void OnCopyCommitFileFullPath(object? s, EventArgs e) => OnCommitFile(s, f => _vm.CopyCommitFilePathAsync(f, relative: false));
    void OnCopyCommitFileRelativePath(object? s, EventArgs e) => OnCommitFile(s, f => _vm.CopyCommitFilePathAsync(f, relative: true));
    void OnViewCommitFileOnHost(object? s, EventArgs e) => OnCommitFile(s, _vm.ViewCommitFileOnHostAsync);

    async void OnAboutClicked(object? sender, EventArgs e) => await ShowAboutAsync();

    Task ShowAboutAsync()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DisplayVersion")?.Value ?? "?";
        return DisplayAlertAsync("CommitDesk", $"Version {version}\n\n" +
            "A desktop git client that works with GitLab™, GitHub and any git server, built with .NET MAUI. " +
            "Formerly GitLab Desktop.\n\n" +
            "Not affiliated with GitLab Inc. or GitHub, Inc. GITLAB is a trademark of GitLab Inc. in the United States " +
            "and other countries and regions. GitHub is a trademark of GitHub, Inc.", "OK");
    }
}
