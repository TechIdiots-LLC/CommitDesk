using System.Windows.Input;
using CommitDesk.Core.Git;
using CommitDesk.Services;
using CommitDesk.ViewModels;

namespace CommitDesk.Views;

/// <summary>
/// The phone layout: on a phone or tablet narrower than <see cref="CompactWidth"/>, one pane at a time (a list, then a
/// file's diff or a commit, with Back), the toolbar sharing the width, a ⋮ menu with the desktop menu bar's actions, and
/// long-press menus with the right-click menus' actions. The desktop layout is never changed: Windows and macOS stay as
/// they are at any width.
/// </summary>
public partial class MainPage
{
    const double CompactWidth = 720;

    /// <summary>Where the phone layout is: a list, a file or commit, or (History) a file in that commit.</summary>
    enum Pane { List, Detail, CommitFile }

    Pane _pane = Pane.List;

    // The desktop column widths, put back when the layout widens again (a tablet turned to landscape)
    GridLength _desktopListWidth = new(340), _desktopCommitFilesWidth = new(280);

    // Touch: tap opens, long-press shows the actions (MainPage.xaml's TouchBehaviors)
    public ICommand OpenFileCommand { get; private set; } = null!;
    public ICommand FileActionsCommand { get; private set; } = null!;
    public ICommand OpenCommitCommand { get; private set; } = null!;
    public ICommand CommitActionsCommand { get; private set; } = null!;
    public ICommand OpenCommitFileCommand { get; private set; } = null!;
    public ICommand CommitFileActionsCommand { get; private set; } = null!;

    /// <summary>Called by the constructor before the page's bindings read the commands.</summary>
    void CreateTouchCommands()
    {
        OpenFileCommand = new Command<ChangedFileViewModel>(f =>
        {
            _vm.SelectedChange = f;
            ShowPane(Pane.Detail, f.Change.Path);
        });
        FileActionsCommand = new Command<ChangedFileViewModel>(async f => await ShowFileActionsAsync(f));
        OpenCommitCommand = new Command<CommitInfo>(c =>
        {
            _vm.SelectedCommit = c;
            ShowPane(Pane.Detail, c.Summary);
        });
        CommitActionsCommand = new Command<CommitInfo>(async c => await ShowCommitActionsAsync(c));
        OpenCommitFileCommand = new Command<FileChange>(f =>
        {
            _vm.SelectedCommitFile = f;
            ShowPane(Pane.CommitFile, f.Path);
        });
        CommitFileActionsCommand = new Command<FileChange>(async f => await ShowCommitFileActionsAsync(f));
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        var compact = (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()) && width > 0 && width < CompactWidth;
        if (compact == _vm.IsCompact) return;
        if (compact)
        {
            // Remember the desktop widths, then go to the list
            _desktopListWidth = RepoGrid.ColumnDefinitions[0].Width;
            _desktopCommitFilesWidth = HistoryFilesGrid.ColumnDefinitions[0].Width;
            _vm.IsDiffExpanded = false;
        }
        _vm.IsCompact = compact;
        _pane = Pane.List;
        ApplyCompactLayout();
    }

    void ShowPane(Pane pane, string? title = null)
    {
        if (!_vm.IsCompact) return;
        _pane = pane;
        if (title is not null) BackTitle.Text = title;
        ApplyCompactLayout();
    }

    /// <summary>One level back: a file in a commit → the commit → the list. False when already at the list.</summary>
    bool GoBack()
    {
        if (!_vm.IsCompact || _pane == Pane.List) return false;
        _pane = _pane == Pane.CommitFile ? Pane.Detail : Pane.List;
        if (_pane == Pane.Detail) BackTitle.Text = _vm.SelectedCommit?.Summary;
        ApplyCompactLayout();
        return true;
    }

    void OnBackClicked(object? sender, EventArgs e) => GoBack();

    // Android's back button and gesture: back through the panes before leaving the app
    protected override bool OnBackButtonPressed() => GoBack() || base.OnBackButtonPressed();

    void ApplyCompactLayout()
    {
        var compact = _vm.IsCompact;
        var toolbar = Toolbar.ColumnDefinitions;
        // Repository | branch | sync | (busy) | ⋮
        double[] desktop = [300, 1, 300, 1, 240];
        for (int i = 0; i < 5; i++)
            toolbar[i].Width = compact ? (i is 0 or 2 ? GridLength.Star : i == 4 ? GridLength.Auto : new GridLength(1)) : new GridLength(desktop[i]);
        toolbar[5].Width = compact ? new GridLength(0) : GridLength.Star;
        toolbar[6].Width = compact ? GridLength.Auto : new GridLength(0);

        var lists = RepoGrid.ColumnDefinitions;
        var commitFiles = HistoryFilesGrid.ColumnDefinitions;
        if (compact)
        {
            var showList = _pane == Pane.List;
            lists[0].Width = showList ? GridLength.Star : new GridLength(0);
            lists[2].Width = showList ? new GridLength(0) : GridLength.Star;
            // History: the commit (message and files) first, then a file's diff full-width
            commitFiles[0].Width = _pane == Pane.CommitFile ? new GridLength(0) : GridLength.Star;
            commitFiles[2].Width = _pane == Pane.CommitFile ? GridLength.Star : new GridLength(0);
        }
        else
        {
            lists[0].Width = _desktopListWidth;
            lists[2].Width = GridLength.Star;
            commitFiles[0].Width = _desktopCommitFilesWidth;
            commitFiles[2].Width = GridLength.Star;
        }
        LeftSplitter.IsVisible = !compact && _vm.ShowLists;
        BackBar.IsVisible = compact && _pane != Pane.List && _vm.HasRepository;

        // A column width change alone doesn't re-run the grid's layout
        ((IView)Toolbar).InvalidateMeasure();
        ((IView)RepoGrid).InvalidateMeasure();
        ((IView)HistoryFilesGrid).InvalidateMeasure();
    }

    /// <summary>Back to the list when what the pane showed goes away (another repository, the other tab).</summary>
    void OnCompactStateChanged(string? property)
    {
        if (!_vm.IsCompact || _pane == Pane.List) return;
        if (property is nameof(MainViewModel.IsHistoryTab) or nameof(MainViewModel.HasRepository) or nameof(MainViewModel.RepoName))
        {
            _pane = Pane.List;
            ApplyCompactLayout();
        }
    }

    // ── Menus ────────────────────────────────────────────────────────────────

    /// <summary>A menu entry: its text, and what it does (a command, which also says whether it's available now).</summary>
    readonly record struct MenuAction(string Text, ICommand? Command = null, Func<Task>? Run = null)
    {
        public bool IsAvailable => Run is not null || Command?.CanExecute(null) == true;

        public async Task InvokeAsync()
        {
            if (Run is not null) await Run();
            else Command?.Execute(null);
        }
    }

    /// <summary>An action sheet of the available entries; runs the one picked.</summary>
    async Task ShowMenuAsync(string title, IEnumerable<MenuAction> actions)
    {
        var available = actions.Where(a => a.IsAvailable).ToList();
        var picked = await DisplayActionSheetAsync(title, "Cancel", null, available.Select(a => a.Text).ToArray());
        var action = available.FirstOrDefault(a => a.Text == picked);
        if (action.Text is not null) await action.InvokeAsync();
    }

    /// <summary>The ⋮ menu: the desktop menu bar's groups, then that group's actions.</summary>
    async void OnMoreClicked(object? sender, EventArgs e)
    {
        var vm = _vm;
        var groups = new List<(string Name, MenuAction[] Actions)>();
        if (vm.HasRepository)
        {
            groups.Add(("Repository", [
                new("Push", vm.PushCommand), new("Force push…", vm.ForcePushCommand), new("Pull", vm.PullCommand), new("Fetch", vm.FetchCommand),
                new("Refresh", vm.RefreshCommand),
                new("Switch repository…", vm.ChooseRepositoryCommand),
                new("Repository settings…", vm.RepositorySettingsCommand),
                new(vm.ViewOnHostText, vm.ViewOnHostCommand), new(vm.CreateIssueText, vm.CreateIssueCommand),
                new(vm.ViewIssuesText, vm.ViewIssuesCommand), new(vm.ViewChangeRequestsText, vm.ViewChangeRequestsCommand),
                new(vm.ViewCiListText, vm.ViewCiListCommand),
                new("Copy remote URL", vm.CopyRemoteUrlCommand),
                new("Remove from CommitDesk…", vm.RemoveRepositoryCommand)]));
            groups.Add(("Branch", [
                new("Switch branch…", vm.ChooseBranchCommand), new("New branch…", vm.NewBranchCommand),
                new("Rename…", vm.RenameBranchCommand), new("Delete…", vm.DeleteBranchCommand),
                new("Discard all changes…", vm.DiscardAllChangesCommand), new("Stash all changes", vm.StashAllChangesCommand),
                new("Restore stashed changes…", vm.RestoreStashCommand),
                new(vm.UpdateFromDefaultText, vm.UpdateFromDefaultCommand),
                new("Merge into current branch…", vm.MergeIntoCurrentCommand),
                new("Squash and merge into current branch…", vm.SquashMergeIntoCurrentCommand),
                new("Rebase current branch…", vm.RebaseCurrentCommand),
                new("Continue merge / rebase", vm.ContinueOperationCommand), new("Abort merge / rebase…", vm.AbortOperationCommand),
                new(vm.CreateChangeRequestText, vm.CreateChangeRequestCommand), new(vm.ViewChangeRequestText, vm.ViewChangeRequestCommand),
                new(vm.ViewBranchOnHostText, vm.ViewBranchOnHostCommand), new(vm.ViewCiText, vm.ViewCiCommand),
                new("Copy branch name", vm.CopyBranchNameCommand)]));
        }
        groups.Add(("Repositories", [
            new("Clone repository…", vm.CloneRepositoryCommand), new("New repository…", vm.NewRepositoryCommand),
            new("Options…", vm.OptionsCommand)]));
        groups.Add(("Help", [
            new("Documentation", Run: () => Launcher.Default.OpenAsync(new Uri(AppUpdater.DocumentationUrl))),
            .. AppUpdater.IsSupported ? new MenuAction[] { new("Check for updates…", Run: () => _updater.CheckAsync(interactive: true)) } : [],
            new("Git command log", vm.ShowCommandLogCommand), new("Third-party notices", vm.ShowNoticesCommand),
            new("About CommitDesk", Run: ShowAboutAsync)]));

        var group = await DisplayActionSheetAsync(vm.HasRepository ? vm.RepoName : "CommitDesk", "Cancel", null,
            groups.Select(g => g.Name).ToArray());
        if (groups.FirstOrDefault(g => g.Name == group) is { Actions: { } actions } chosen)
            await ShowMenuAsync(chosen.Name, actions);
    }

    Task ShowFileActionsAsync(ChangedFileViewModel f) => ShowMenuAsync(f.Change.FileName, [
        new("Discard changes…", Run: () => _vm.DiscardFileAsync(f)),
        new("Ignore file (add to .gitignore)", Run: () => _vm.IgnoreFileAsync(f)),
        new("Ignore all files with this extension", Run: () => _vm.IgnoreExtensionAsync(f)),
        new("Copy diff", Run: () => _vm.CopyFileDiffAsync(f)),
        new("Copy file path", Run: () => _vm.CopyFilePathAsync(f, relative: false)),
        new("Copy relative file path", Run: () => _vm.CopyFilePathAsync(f, relative: true))]);

    Task ShowCommitActionsAsync(CommitInfo c) => ShowMenuAsync(c.Summary, [
        new("Amend commit…", Run: () => _vm.AmendCommitAsync(c)),
        new("Reset to commit…", Run: () => _vm.ResetToCommitAsync(c)),
        new("Checkout commit", Run: () => _vm.CheckoutCommitAsync(c)),
        new("Revert changes in commit", Run: () => _vm.RevertCommitAsync(c)),
        new("Create branch from commit", Run: () => _vm.CreateBranchFromCommitAsync(c)),
        new("Create tag…", Run: () => _vm.CreateTagAsync(c)),
        new("Cherry-pick commit…", Run: () => _vm.CherryPickCommitAsync(c)),
        new("Copy SHA", Run: () => _vm.CopyShaAsync(c)),
        new("Copy commit as patch", Run: () => _vm.CopyCommitPatchAsync(c)),
        .. c.Tags.Count > 0 ? new MenuAction[] { new("Copy tag", Run: () => _vm.CopyTagAsync(c)) } : [],
        new("View commit in browser", Run: () => _vm.ViewCommitOnHostAsync(c))]);

    Task ShowCommitFileActionsAsync(FileChange f) => ShowMenuAsync(f.FileName, [
        new("Copy diff", Run: () => _vm.CopyCommitFileDiffAsync(f)),
        new("Copy file path", Run: () => _vm.CopyCommitFilePathAsync(f, relative: false)),
        new("Copy relative file path", Run: () => _vm.CopyCommitFilePathAsync(f, relative: true)),
        new(_vm.ViewOnHostText, Run: () => _vm.ViewCommitFileOnHostAsync(f))]);
}
