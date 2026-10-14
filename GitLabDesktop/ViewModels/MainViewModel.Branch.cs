using CommunityToolkit.Mvvm.Input;
using GitLabDesktop.Core.Git;
using GitLabDesktop.Core.Hosting;
using GitLabDesktop.Services;
using GitLabDesktop.Views;

namespace GitLabDesktop.ViewModels;

// Branch menu: create/switch/rename/delete, merge, rebase, stash, and merge/pull requests.
public sealed partial class MainViewModel
{
    const string StashPrefix = "!!GitLabDesktop<";
    static readonly object NewBranchMarker = new();

    public string UpdateFromDefaultText => $"Update from {DefaultBranch}";

    string DefaultBranch => _defaultBranch ?? "main";

    static string StashMessageFor(string branch) => $"{StashPrefix}{branch}>";

    /// <summary>Picks a local or remote branch other than the current one.</summary>
    async Task<BranchInfo?> PickBranchAsync(string title, bool includeRemote = true)
    {
        var branches = await Repo!.GetBranchesAsync();
        var items = branches
            .Where(b => !b.IsCurrent && (includeRemote || !b.IsRemote))
            .Select(b => new PickerItem(b.Name, b.IsRemote ? "remote" : b.Upstream is { } u ? $"tracks {u}" : null, b,
                b.LastCommitDate is { } d ? Converters.RelativeTimeConverter.Format(d) : null));
        return await _dialogs.PickAsync<BranchInfo>(title, items, "No other branches");
    }

    [RelayCommand]
    async Task ChooseBranch()
    {
        if (Repo is null) return;
        var branches = await Repo.GetBranchesAsync();
        var localNames = branches.Where(b => !b.IsRemote).Select(b => b.Name).ToHashSet();

        static string? Age(BranchInfo b) => b.LastCommitDate is { } d ? Converters.RelativeTimeConverter.Format(d) : null;

        // Grouped like GitHub Desktop: the default branch, then other local branches, then remote-only ones.
        var local = branches.Where(b => !b.IsRemote)
            .OrderBy(b => b.Name == DefaultBranch ? 0 : 1)
            .Select(b => new PickerItem(b.IsCurrent ? $"✓ {b.Name}" : b.Name,
                b.Upstream is { } u ? $"tracks {u}" : "not published", b, Age(b),
                b.Name == DefaultBranch ? "Default branch" : "Branches"));
        var remote = branches.Where(b => b.IsRemote && !localNames.Contains(b.NameWithoutRemote))
            .Select(b => new PickerItem(b.Name, null, b, Age(b), "Remote branches"));

        var picked = await _dialogs.DropdownAsync<object>(DropdownAnchor.Branch, "Switch branch", local.Concat(remote),
            [new PickerItem("New branch", null, NewBranchMarker)], secondTab: ChangeRequestsTab());
        if (picked == NewBranchMarker) await NewBranchAsync(null, null);
        else if (picked is BranchInfo b && !b.IsCurrent) await SwitchToBranchAsync(b);
        else if (picked is OpenChangeRequest request) await CheckoutChangeRequestAsync(request);
    }

    static readonly Converters.PipelineStatusGlyphConverter CiGlyph = new();
    static readonly Converters.PipelineStatusColorConverter CiColor = new();

    /// <summary>
    /// The branch dropdown's "Pull requests" / "Merge requests" tab, like GitHub Desktop's: the project's open requests,
    /// then their CI status as it arrives. Null when the remote isn't on GitLab or GitHub.
    /// </summary>
    PickerTabSource? ChangeRequestsTab()
    {
        if (Remote is not { Kind: not HostingKind.Unknown } remote) return null;
        var title = char.ToUpper(remote.ChangeRequestsName[0]) + remote.ChangeRequestsName[1..];
        var group = $"{title} in {remote.ProjectPath}";

        var needAccount = $"Add a {remote.ProviderName} account with an access token in File › Options to see {remote.ChangeRequestsName} here.";
        // Without an account, a public github.com repository can still be listed (GitHub allows a few anonymous calls).
        var signedIn = HostingService is not null;
        if ((HostingService ?? _hosting.AnonymousFor(remote)) is not { } service)
            return new PickerTabSource(title, "", _ => Single(new PickerTabUpdate([], EmptyText: needAccount)));

        PickerItem Item(OpenChangeRequest r, CiStatus? ci) => new(r.Title,
            $"{r.Reference} opened {Converters.RelativeTimeConverter.Format(r.CreatedAt)} by {r.Author}{(r.Draft ? " • Draft" : "")}",
            r, ci is null ? null : (string)CiGlyph.Convert(ci.Status, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture),
            group, ci is null ? null : (Color)CiColor.Convert(ci.Status, typeof(Color), null, System.Globalization.CultureInfo.CurrentCulture));

        async IAsyncEnumerable<PickerTabUpdate> Load([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            IReadOnlyList<OpenChangeRequest> requests;
            if (signedIn)
            {
                requests = await service.ListOpenChangeRequestsAsync(remote.ProjectPath, ct);
            }
            else
            {
                // A private repository (404) or the anonymous allowance used up (403): ask for an account instead.
                var refused = false;
                try { requests = await service.ListOpenChangeRequestsAsync(remote.ProjectPath, ct); }
                catch (Core.GitHub.GitHubApiException) { requests = []; refused = true; }
                if (refused)
                {
                    yield return new PickerTabUpdate([], EmptyText: needAccount);
                    yield break;
                }
            }
            var counted = $"{title} ({requests.Count})";
            var empty = $"No open {remote.ChangeRequestsName} in {remote.ProjectPath}.";
            yield return new PickerTabUpdate(requests.Select(r => Item(r, null)).ToList(), counted, empty);
            if (!signedIn) yield break;   // one call per request would use up the anonymous allowance

            // CI status per request: one call each, so only the newest, a few at a time
            var statuses = new CiStatus?[requests.Count];
            using var gate = new SemaphoreSlim(6);
            await Task.WhenAll(requests.Take(50).Select(async (r, i) =>
            {
                await gate.WaitAsync(ct);
                try { statuses[i] = await service.GetChangeRequestCiStatusAsync(remote.ProjectPath, r, ct); }
                catch (Exception) when (!ct.IsCancellationRequested) { }   // a status is a nicety; the list stands without it
                finally { gate.Release(); }
            }));
            if (statuses.Any(s => s is not null))
                yield return new PickerTabUpdate(requests.Select((r, i) => Item(r, statuses[i])).ToList(), counted, empty);
        }
        return new PickerTabSource(title, $"Loading {remote.ChangeRequestsName}…", Load);
    }

    static async IAsyncEnumerable<PickerTabUpdate> Single(PickerTabUpdate update)
    {
        await Task.CompletedTask;
        yield return update;
    }

    Task SwitchToBranchAsync(BranchInfo target)
        => SwitchToAsync(target.IsRemote ? target.NameWithoutRemote : target.Name, () => Repo!.CheckoutAsync(target));

    /// <summary>Checks out an open merge/pull request's branch (from a fork: "pr/12" or "mr/12" tracking the fork).</summary>
    Task CheckoutChangeRequestAsync(OpenChangeRequest request)
        => SwitchToAsync(request.FromFork ? request.ForkBranchName : request.SourceBranch,
            () => Repo!.CheckoutChangeRequestAsync(RemoteName, request, preferSsh: _settings.CloneWithSsh));

    /// <summary>Switches branch, first asking whether uncommitted changes stay on the current branch or come along.</summary>
    async Task SwitchToAsync(string targetName, Func<Task> checkout)
    {
        var current = Status?.Branch;
        if (current == targetName && ChangedFiles.Count == 0)
        {
            await RunAsync($"Updating {targetName}…", checkout);
            return;
        }

        if (ChangedFiles.Count > 0 && current is not null && current != targetName)
        {
            var leave = $"Leave my changes on {current}";
            var bring = $"Bring my changes to {targetName}";
            var choice = await _dialogs.ChooseAsync("You have uncommitted changes", leave, bring);
            if (choice is null) return;
            if (choice == leave)
            {
                var ok = false;
                await RunAsync("Stashing…", async () =>
                {
                    await Repo!.StashAsync(StashMessageFor(current));
                    ok = true;
                }, refresh: false);
                if (!ok) return;
            }
        }

        await RunAsync($"Switching to {targetName}…", checkout);

        // Offer back changes that were left on this branch earlier.
        var stashes = await Repo!.GetStashesAsync();
        var mine = stashes.FirstOrDefault(s => s.Message.EndsWith(StashMessageFor(targetName)));
        if (mine is not null &&
            await _dialogs.ConfirmAsync("Stashed changes", $"You left changes on {targetName}. Restore them now?", "Restore", "Later"))
        {
            await RunAsync("Restoring changes…", () => Repo!.PopStashAsync(mine.Name));
        }
        await LoadHostingInfoAsync();
    }

    static string SanitizeBranchName(string name)
        => string.Join('-', name.Trim().Split((char[])[' ', '\t', '~', '^', ':', '?', '*', '[', '\\'], StringSplitOptions.RemoveEmptyEntries));

    async Task NewBranchAsync(string? startPoint, string? description)
    {
        if (Repo is null) return;
        var name = await _dialogs.PromptAsync("Create a branch",
            description is null ? $"Name (based on {BranchName}):" : $"Name ({description}):",
            accept: "Create branch", placeholder: "feature/my-change");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = SanitizeBranchName(name);
        await RunAsync("Creating branch…", () => Repo.CreateBranchAsync(name, startPoint));
        await LoadHostingInfoAsync();
    }

    [RelayCommand]
    Task NewBranch() => NewBranchAsync(null, null);

    [RelayCommand]
    async Task RenameBranch()
    {
        if (Repo is null || !await RequireBranchAsync()) return;
        var current = Status!.Branch!;
        var name = await _dialogs.PromptAsync("Rename branch", $"New name for {current}:", current, "Rename");
        if (string.IsNullOrWhiteSpace(name) || name == current) return;
        await RunAsync("Renaming…", () => Repo.RenameBranchAsync(current, SanitizeBranchName(name)));
    }

    [RelayCommand]
    async Task DeleteBranch()
    {
        if (Repo is null) return;
        var branch = await PickBranchAsync("Delete which branch?", includeRemote: false);
        if (branch is null) return;

        string? choice;
        if (branch.Upstream is { } upstream)
        {
            var local = "Delete local branch only";
            var both = $"Delete local and {upstream}";
            choice = await _dialogs.ChooseAsync($"Delete {branch.Name}?", local, both);
            if (choice is null) return;
            await RunAsync("Deleting…", async () =>
            {
                await Repo.DeleteBranchAsync(branch.Name);
                if (choice == both)
                {
                    var remote = upstream[..upstream.IndexOf('/')];
                    await Repo.DeleteRemoteBranchAsync(remote, upstream[(upstream.IndexOf('/') + 1)..]);
                }
            });
        }
        else if (await _dialogs.ConfirmAsync("Delete branch", $"Delete {branch.Name}? It has not been published, so its commits may be lost.", "Delete"))
        {
            await RunAsync("Deleting…", () => Repo.DeleteBranchAsync(branch.Name));
        }
    }

    [RelayCommand]
    async Task DiscardAllChanges()
    {
        if (Repo is null || ChangedFiles.Count == 0) return;
        if (!await _dialogs.ConfirmAsync("Discard all changes",
                $"Discard all {ChangedFiles.Count} changed files? This cannot be undone.", "Discard all"))
            return;
        await RunAsync("Discarding…", () => Repo.DiscardAsync(ChangedFiles.Select(f => f.Change).ToList()));
    }

    [RelayCommand]
    Task StashAllChanges()
    {
        if (Repo is null || ChangedFiles.Count == 0 || Status?.Branch is not { } branch) return Task.CompletedTask;
        return RunAsync("Stashing…", () => Repo.StashAsync(StashMessageFor(branch)));
    }

    [RelayCommand]
    async Task RestoreStash()
    {
        if (Repo is null) return;
        var stashes = await Repo.GetStashesAsync();
        var items = stashes.Select(s => new PickerItem(
            s.Message.Contains(StashPrefix) ? $"Changes left on {s.Message[(s.Message.IndexOf(StashPrefix) + StashPrefix.Length)..].TrimEnd('>')}" : s.Message,
            s.Name, s));
        var picked = await _dialogs.PickAsync<StashEntry>("Restore stashed changes", items, "No stashed changes");
        if (picked is not null) await RunAsync("Restoring…", () => Repo.PopStashAsync(picked.Name));
    }

    [RelayCommand]
    Task UpdateFromDefault()
    {
        if (Repo is null) return Task.CompletedTask;
        return RunAsync($"Updating from {DefaultBranch}…", async () =>
        {
            await Repo.FetchAsync(RemoteName);
            _lastFetched = DateTimeOffset.Now;
            await Repo.MergeAsync($"{RemoteName}/{DefaultBranch}");
        });
    }

    [RelayCommand]
    async Task MergeIntoCurrent()
    {
        if (Repo is null || !await RequireBranchAsync()) return;
        var b = await PickBranchAsync($"Merge into {Status!.Branch}");
        if (b is not null) await RunAsync($"Merging {b.Name}…", () => Repo.MergeAsync(b.Name));
    }

    [RelayCommand]
    async Task SquashMergeIntoCurrent()
    {
        if (Repo is null || !await RequireBranchAsync()) return;
        var b = await PickBranchAsync($"Squash and merge into {Status!.Branch}");
        if (b is null) return;
        await RunAsync($"Squashing {b.Name}…", async () =>
        {
            await Repo.SquashMergeAsync(b.Name);
            // Leave the squashed changes for review; the commit message is prefilled from SQUASH_MSG.
            SetCommitMessage($"Squashed commit of '{b.Name}'\n\n" + (await Repo.GetPreparedCommitMessageAsync() ?? ""));
        });
        IsHistoryTab = false;
    }

    [RelayCommand]
    async Task RebaseCurrent()
    {
        if (Repo is null || !await RequireBranchAsync()) return;
        var b = await PickBranchAsync($"Rebase {Status!.Branch} onto");
        if (b is null) return;
        if (ChangedFiles.Count > 0)
        {
            await _dialogs.AlertAsync("Rebase", "Commit or stash your changes before rebasing.");
            return;
        }
        await RunAsync($"Rebasing onto {b.Name}…", () => Repo.RebaseAsync(b.Name));
    }

    [RelayCommand]
    async Task AbortOperation()
    {
        if (Repo is null || Operation == RepositoryOperation.None) return;
        if (!await _dialogs.ConfirmAsync("Abort", $"Abort the {Operation.ToString().ToLowerInvariant()} and return to the previous state?", "Abort"))
            return;
        await RunAsync("Aborting…", () => Repo.AbortAsync(Operation));
        CommitSummary = "";
        CommitDescription = "";
    }

    public bool HasConflicts => ChangedFiles.Any(f => f.Change.Kind == FileChangeKind.Conflicted);

    bool _showingConflicts;

    /// <summary>The Resolve conflicts dialog, like GitHub Desktop's: every conflicted file, rechecked as it's saved.</summary>
    [RelayCommand]
    async Task ResolveConflicts()
    {
        if (Repo is not { } repo || _showingConflicts) return;
        var paths = ChangedFiles.Where(f => f.Change.Kind == FileChangeKind.Conflicted).Select(f => f.Change.Path).ToList();
        if (paths.Count == 0) return;

        _showingConflicts = true;
        ConflictsOutcome outcome;
        try
        {
            var vm = new ConflictsViewModel(repo, Operation, paths, _dialogs,
                path => TryPlatform(() => _platform.OpenInEditor(System.IO.Path.Combine(repo.Path, path))));
            await _dialogs.PushModalAsync(new ConflictsPage(vm));
            outcome = await vm.Result;
        }
        finally
        {
            _showingConflicts = false;
        }

        if (outcome == ConflictsOutcome.Continue) await ContinueResolvedAsync();
        else if (outcome == ConflictsOutcome.Abort) await AbortOperation();
        else await RefreshAsync();
    }

    [RelayCommand]
    async Task ContinueOperation()
    {
        if (Repo is null || Operation == RepositoryOperation.None) return;
        // Conflicted files with no markers left count as resolved (Continue stages them); anything else needs the dialog.
        foreach (var f in ChangedFiles.Where(f => f.Change.Kind == FileChangeKind.Conflicted).ToList())
        {
            var c = await Repo.GetConflictAsync(f.Change.Path);
            if (c.Markers > 0 || !c.HasOurs || !c.HasTheirs || !c.ExistsOnDisk)
            {
                await ResolveConflicts();
                return;
            }
        }
        await ContinueResolvedAsync();
    }

    Task ContinueResolvedAsync() => Repo is null ? Task.CompletedTask : RunAsync("Continuing…", async () =>
    {
        await Repo.ContinueAsync(Operation);
        CommitSummary = "";
        CommitDescription = "";
    });

    // ── GitLab / GitHub ──────────────────────────────────────────────────────

    [RelayCommand]
    async Task CompareToBranch()
    {
        if (Repo is null || !await RequireRemoteAsync() || !await RequireBranchAsync()) return;
        var b = await PickBranchAsync($"Compare to branch on {ProviderName}");
        if (b is not null) await OpenLinkAsync(Remote!.CompareLink(b.NameWithoutRemote, Status!.Branch!));
    }

    [RelayCommand]
    async Task CompareOnHost()
    {
        if (await RequireRemoteAsync() && await RequireBranchAsync())
            await OpenLinkAsync(Remote!.CompareLink(DefaultBranch, Status!.Branch!));
    }

    [RelayCommand]
    async Task ViewBranchOnHost()
    {
        if (await RequireRemoteAsync() && await RequireBranchAsync())
            await OpenLinkAsync(Remote!.BranchLink(Status!.Branch!));
    }

    /// <summary>
    /// With an account, only when the branch has a request (open, merged or closed), as found when the branch was last
    /// refreshed, fetched or pulled. Without one there is no way to know beforehand, so it stays available and looks.
    /// </summary>
    bool CanViewChangeRequest() => ViewableChangeRequest is not null || HostingService is null;

    [RelayCommand(CanExecute = nameof(CanViewChangeRequest))]
    async Task ViewChangeRequest()
    {
        if (!await RequireRemoteAsync()) return;
        if (ViewableChangeRequest is null) await LoadHostingInfoAsync();
        if (ViewableChangeRequest is { } known)
        {
            await _platform.OpenUrlAsync(known.WebUrl);
            return;
        }
        // Look through the open requests: finds one from a fork too, and needs no account for a public github.com project
        var service = HostingService ?? _hosting.AnonymousFor(Remote);
        var found = await FindChangeRequestForBranchAsync(service);
        // Then a merged or closed one from the branch
        if (found is null && service is not null && Status?.Branch is { } current)
            found = await FindPastChangeRequestAsync(service, Remote!.ProjectPath, current);
        if (found is not null)
        {
            await _platform.OpenUrlAsync(found.WebUrl);
            return;
        }
        var name = Remote!.ChangeRequestName;
        if (HostingService is null)
        {
            // Couldn't check (no account): the requests from this branch, rather than every request
            await OpenLinkAsync(Status?.Branch is { } branch ? Remote.ChangeRequestsForBranchLink(branch) : Remote.ChangeRequestsLink);
            return;
        }
        if (await _dialogs.ConfirmAsync($"No {name}", $"There is no open {name} for {BranchName}. Create one?", $"Create {name}"))
            await CreateChangeRequest();
    }

    /// <summary>
    /// The open request the current branch belongs to, found among all the project's open requests (see
    /// <see cref="OpenChangeRequest.ForBranch"/>): unlike a lookup by branch name, this finds one from a fork.
    /// </summary>
    async Task<ChangeRequest?> FindChangeRequestForBranchAsync(IHostingService? service, bool lookUpDirectly = true)
    {
        if (service is null || Remote is not { } remote || Status is not { Branch: { } branch } status) return null;
        try
        {
            // The exact lookup first (a request from the project's own branch: one call, however many are open)
            if (lookUpDirectly && await service.FindOpenChangeRequestAsync(remote.ProjectPath, branch) is { } direct) return direct;
            var requests = await service.ListOpenChangeRequestsAsync(remote.ProjectPath);
            return OpenChangeRequest.ForBranch(requests, branch, [status.HeadSha])?.ToChangeRequest();
        }
        catch (Exception)
        {
            return null;   // e.g. a private repository without an account: the caller falls back to a link
        }
    }

    /// <summary>The branch's latest merged or closed request, or null (also when the server can't be asked).</summary>
    static async Task<ChangeRequest?> FindPastChangeRequestAsync(IHostingService service, string projectPath, string branch)
    {
        try
        {
            return await service.FindLatestChangeRequestAsync(projectPath, branch) is { IsOpen: false } past ? past : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [RelayCommand]
    async Task ViewCi()
    {
        if (!await RequireRemoteAsync() || !await RequireBranchAsync()) return;
        await OpenLinkAsync(Ci?.WebUrl ?? Remote!.CiLink(Status!.Branch!));
    }

    [RelayCommand]
    async Task CreateChangeRequest()
    {
        if (Repo is null || !await RequireRemoteAsync() || !await RequireBranchAsync()) return;
        var branch = Status!.Branch!;
        var remote = Remote!;
        var name = remote.ChangeRequestName;

        if (HostingService is not { } service)
        {
            // No account for this server: use the host's own "new merge/pull request" page.
            await OpenLinkAsync(remote.NewChangeRequestLink(branch, DefaultBranch));
            return;
        }

        if (ChangeRequest is not null)
        {
            if (await _dialogs.ConfirmAsync($"{char.ToUpper(name[0])}{name[1..]} exists", $"{ChangeRequestText} is already open for {branch}. View it?", "View"))
                await _platform.OpenUrlAsync(ChangeRequest.WebUrl);
            return;
        }

        if (Status.Upstream is null || Status.Ahead > 0)
        {
            var what = Status.Upstream is null ? "has not been published" : $"has {Status.Ahead} unpushed commit(s)";
            if (!await _dialogs.ConfirmAsync("Push first?", $"{branch} {what}. Push it before creating the {name}?", "Push", "Skip"))
            {
                if (Status.Upstream is null) return;   // the server needs the branch to exist
            }
            else
            {
                await PushAsync(force: false);
                if (Status?.Upstream is null || Status.Ahead > 0) return;   // push failed
            }
        }

        var vm = new CreateChangeRequestViewModel(service, _platform, Repo, remote, RemoteName, branch, DefaultBranch);
        await vm.InitializeAsync();
        await _dialogs.PushModalAsync(new CreateChangeRequestPage(vm));
        var created = await vm.Result;
        if (created is not null) ChangeRequest = created;
    }
}
