using System.Reflection;
using GitLabDesktop.Core.Updates;
using GitLabDesktop.Views;

namespace GitLabDesktop.Services;

/// <summary>
/// Checks this app's release feeds (GitLab, then GitHub) for a newer version and offers it. Installed Windows copies
/// update in place with the setup.exe; macOS opens the .dmg download and Android the APK. Runs at startup unless turned
/// off in Options, and from Help › Check for updates.
/// </summary>
public sealed class AppUpdater(DialogService dialogs)
{
    public const string ProductName = "GitLab Desktop";

    // Where this app itself is released (not the servers users work with).
    const string GitLabServer = "https://gitlab.techidiots.net";
    const string GitLabProject = "techidiots-llc/gitlabdesktop";
    const string GitHubRepo = "TechIdiots-LLC/GitlabDesktop";

    /// <summary>The user guide (docs/ in this app's repository, rendered by GitLab).</summary>
    public const string DocumentationUrl = $"{GitLabServer}/{GitLabProject}/-/blob/main/docs/index.md";

    const string AutoCheckKey = "update_auto_check";
    const string PrereleaseKey = "update_include_prereleases";
    const string SkippedKey = "update_skipped_version";

    // Also downloads the Windows installer, so no short overall timeout; the feed check has its own
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    readonly UpdateService _service = new(Http, ProductName, CurrentVersion, GitLabServer, GitLabProject, GitHubRepo);
    bool _checking;
    bool _startupCheckDone;

    public static bool AutoCheck
    {
        get => Preferences.Get(AutoCheckKey, true);
        set => Preferences.Set(AutoCheckKey, value);
    }

    /// <summary>Offer pre-releases (e.g. 0.5.0-rc.1). They're offered anyway when running a pre-release.</summary>
    public static bool IncludePrereleases
    {
        get => Preferences.Get(PrereleaseKey, false);
        set => Preferences.Set(PrereleaseKey, value);
    }

    /// <summary>
    /// A release the user chose to skip: the startup check stays quiet about it (a newer release is still offered),
    /// while Help › Check for updates still shows it.
    /// </summary>
    public static string? SkippedVersion
    {
        get => Preferences.Get(SkippedKey, null as string);
        set => Preferences.Set(SkippedKey, value);
    }

    /// <summary>ApplicationDisplayVersion, stamped into the assembly by the csproj.</summary>
    public static string CurrentVersion =>
        typeof(AppUpdater).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "DisplayVersion")?.Value ?? "0.0.0";

#if PLAY_STORE
    /// <summary>The Google Play build updates through Play, which doesn't allow apps it installs to update themselves.</summary>
    public static bool IsSupported => false;
#else
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsAndroid();
#endif

    /// <summary>The release file this copy updates from, or null when it can only be sent to the release page.</summary>
    static string? PlatformAsset
    {
        get
        {
            if (OperatingSystem.IsAndroid()) return "-android-preview.apk";
            if (OperatingSystem.IsMacCatalyst()) return "-macos.dmg";
            if (OperatingSystem.IsWindows() && WindowsUpdateInstaller.IsInstalledCopy) return WindowsUpdateInstaller.InstallerSuffix;
            return null;
        }
    }

    /// <summary>The once-per-launch startup check; does nothing when turned off in Options.</summary>
    public async Task CheckOnStartupAsync()
    {
        if (_startupCheckDone || !AutoCheck) return;
        _startupCheckDone = true;
        await CheckAsync(interactive: false);
    }

    /// <param name="interactive">
    /// True when the user asked: report "up to date" and errors. False at startup: stay quiet unless there is an update.
    /// </param>
    public async Task CheckAsync(bool interactive)
    {
        if (_checking) return;
        if (!IsSupported)
        {
#if PLAY_STORE
            if (interactive) await dialogs.AlertAsync("Check for updates", "Updates are installed through Google Play.");
#else
            if (interactive) await dialogs.AlertAsync("Check for updates", "Update checks aren't available on this platform.");
#endif
            return;
        }

        _checking = true;
        try
        {
            var asset = PlatformAsset;
            var result = await _service.CheckAsync(IncludePrereleases, asset);
            if (result.Update is null)
            {
                if (interactive)
                    await dialogs.AlertAsync("Check for updates",
                        $"You're running the latest version of {ProductName} ({result.CurrentVersion}).");
                return;
            }
            if (!interactive && result.Update.Version.ToString() == SkippedVersion) return;

            var page = new UpdatePage(result, Http, asset);
            await dialogs.PushModalAsync(page);
            // The installer replaces the app's files and starts it again; settings are saved as they change.
            if (await page.Completion) Application.Current?.Quit();
        }
        catch (Exception ex) when (interactive)
        {
            await dialogs.AlertAsync("Check for updates", ex.Message);
        }
        catch (Exception ex)
        {
            // Startup check: offline or a feed outage shouldn't interrupt the user
            System.Diagnostics.Debug.WriteLine($"[AppUpdater] Update check failed: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }
}
