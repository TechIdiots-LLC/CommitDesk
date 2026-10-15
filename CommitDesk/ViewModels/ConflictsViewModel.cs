using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommitDesk.Core.Git;
using CommitDesk.Services;

namespace CommitDesk.ViewModels;

public enum ConflictsOutcome { Closed, Continue, Abort }

/// <summary>One file in the Resolve conflicts dialog.</summary>
public sealed partial class ConflictFileViewModel(string path) : ObservableObject
{
    public string Path { get; } = path;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => Path.Length > FileName.Length ? Path[..^FileName.Length] : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolved), nameof(StatusText), nameof(CanEdit), nameof(CanChooseSide))]
    private ConflictInfo? _info;

    /// <summary>Set when a side was chosen here, for the status text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _resolvedWith;

    public ConflictSides Sides { get; set; } = new("your branch", "the other branch");

    /// <summary>
    /// Resolved once a side was chosen (git no longer has it as unmerged), or once an edited file has no conflict
    /// markers left; no need to mark it. A file deleted on one side always needs a choice.
    /// </summary>
    public bool IsResolved => Info is { } i && (!i.Unmerged || (i.HasOurs && i.HasTheirs && i.ExistsOnDisk && i.Markers == 0));

    public bool CanEdit => Info is { ExistsOnDisk: true, Unmerged: true } && !IsResolved;
    public bool CanChooseSide => Info is { Unmerged: true } && !IsResolved;

    public string StatusText => Info switch
    {
        null => "Checking…",
        { Unmerged: false } => ResolvedWith is null ? "Resolved" : $"Resolved: {ResolvedWith}",
        { HasOurs: false } => $"Deleted in {Sides.Ours}, changed in {Sides.Theirs}",
        { HasTheirs: false } => $"Changed in {Sides.Ours}, deleted in {Sides.Theirs}",
        { Markers: 0 } => "No conflicts remaining",
        { Markers: var n } => $"{n} conflict{(n == 1 ? "" : "s")}",
    };
}

/// <summary>
/// Lists every conflicted file of a merge, rebase, cherry-pick, revert or restored stash, for the
/// Resolve conflicts dialog. The page rechecks the files while it is open, so editing one in an editor turns it
/// resolved without any further step.
/// </summary>
public sealed partial class ConflictsViewModel : ModalViewModel<ConflictsOutcome>
{
    readonly GitRepository _repo;
    readonly DialogService _dialogs;
    readonly Func<string, Task> _openInEditor;
    readonly RepositoryOperation _operation;
    ConflictSides _sides = new("your branch", "the other branch");

    public ConflictsViewModel(GitRepository repo, RepositoryOperation operation, IEnumerable<string> paths,
        DialogService dialogs, Func<string, Task> openInEditor)
    {
        _repo = repo;
        _operation = operation;
        _dialogs = dialogs;
        _openInEditor = openInEditor;
        foreach (var p in paths.Order(StringComparer.OrdinalIgnoreCase)) Files.Add(new ConflictFileViewModel(p));
    }

    protected override ConflictsOutcome CancelledResult => ConflictsOutcome.Closed;

    public ObservableCollection<ConflictFileViewModel> Files { get; } = [];

    public bool HasOperation => _operation != RepositoryOperation.None;

    string Verb => _operation switch
    {
        RepositoryOperation.Merge => "merge",
        RepositoryOperation.Rebase => "rebase",
        RepositoryOperation.CherryPick => "cherry-pick",
        RepositoryOperation.Revert => "revert",
        _ => "",
    };

    public string Heading => HasOperation ? "Resolve conflicts before continuing" : "Resolve conflicts";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(Summary))]
    private int _unresolvedCount;

    public string Summary => UnresolvedCount == 0
        ? HasOperation ? $"All conflicts are resolved. Continue the {Verb} to finish." : "All conflicts are resolved. Commit when you're ready."
        : $"{UnresolvedCount} of {Files.Count} file{(Files.Count == 1 ? " has" : "s have")} conflicts between {_sides.Ours} and {_sides.Theirs}. " +
          "Edit them in your editor (this list updates as you save) or choose a side's version.";

    public bool CanContinue => HasOperation && UnresolvedCount == 0;
    public string ContinueText => $"Continue {Verb}";
    public string AbortText => $"Abort {Verb}";

    bool _checking, _recheckAgain;
    FileSystemWatcher? _watcher;
    IDispatcherTimer? _debounce;

    /// <summary>
    /// Rechecks whenever a conflicted file is saved, so editing one in another program updates the list by itself.
    /// Editors often write a file several times per save (temp file, rename, touch), hence the short debounce.
    /// </summary>
    public void StartWatching(IDispatcher dispatcher)
    {
        StopWatching();
        _debounce = dispatcher.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(400);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => _ = RecheckAsync();

        var watched = Files.Select(f => f.Path.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            _watcher = new FileSystemWatcher(_repo.Path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            void OnChange(string? fullPath)
            {
                if (fullPath is null) return;
                var relative = System.IO.Path.GetRelativePath(_repo.Path, fullPath).Replace('\\', '/');
                if (watched.Contains(relative)) dispatcher.Dispatch(() => { _debounce?.Stop(); _debounce?.Start(); });
            }
            _watcher.Changed += (_, e) => OnChange(e.FullPath);
            _watcher.Created += (_, e) => OnChange(e.FullPath);
            _watcher.Deleted += (_, e) => OnChange(e.FullPath);
            _watcher.Renamed += (_, e) => { OnChange(e.FullPath); OnChange(e.OldFullPath); };
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // No change notifications here (e.g. some network drives): fall back to checking every few seconds.
            _watcher?.Dispose();
            _watcher = null;
            _debounce.Interval = TimeSpan.FromSeconds(3);
            _debounce.IsRepeating = true;
            _debounce.Start();
        }
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Stop();
        _debounce = null;
    }

    /// <summary>Reloads every file's state: when the dialog opens, after a choice, and when a file is saved.</summary>
    public async Task RecheckAsync()
    {
        if (_checking)
        {
            _recheckAgain = true;   // a save during a check: check again afterwards so it isn't missed
            return;
        }
        _checking = true;
        try
        {
            _sides = await _repo.GetConflictSidesAsync(_operation);
            foreach (var f in Files)
            {
                f.Sides = _sides;
                f.Info = await _repo.GetConflictAsync(f.Path);
            }
            UnresolvedCount = Files.Count(f => !f.IsResolved);
            OnPropertyChanged(nameof(Summary));
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            _checking = false;
        }
        if (_recheckAgain)
        {
            _recheckAgain = false;
            await RecheckAsync();
        }
    }

    [RelayCommand]
    Task OpenInEditor(ConflictFileViewModel file) => _openInEditor(file.Path);

    [RelayCommand]
    async Task ChooseSide(ConflictFileViewModel file)
    {
        if (file.Info is not { } info) return;
        string Option(bool has, string side) => has ? $"Use {side}'s version" : $"Delete it, as in {side}";
        var ours = Option(info.HasOurs, _sides.Ours);
        var theirs = Option(info.HasTheirs, _sides.Theirs);
        var choice = await _dialogs.ChooseAsync(file.Path, ours, theirs);
        if (choice is null) return;
        try
        {
            await _repo.ResolveConflictAsync(file.Path, choice == ours ? ConflictChoice.Ours : ConflictChoice.Theirs);
            file.ResolvedWith = choice == ours ? $"{_sides.Ours}'s version" : $"{_sides.Theirs}'s version";
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        await RecheckAsync();
    }

    [RelayCommand]
    async Task Continue()
    {
        await RecheckAsync();
        if (CanContinue) Complete(ConflictsOutcome.Continue);
    }

    [RelayCommand]
    void Abort() => Complete(ConflictsOutcome.Abort);

    [RelayCommand]
    void Close() => Cancel();
}
