using System.Diagnostics;

namespace CommitDesk.Services;

/// <summary>
/// The app was called GitLab Desktop up to 0.6.x, and its settings live in places named after that. Runs once at start,
/// before anything reads a setting, and copies them to CommitDesk's places when CommitDesk has none of its own yet:
/// <list type="bullet">
/// <item>Windows: the old app data folder (settings, and saved sign-ins, which Windows encrypts for the user rather than
/// the app). The old link registrations are replaced by CommitDesk's.</item>
/// <item>macOS: the old preferences. Saved sign-ins are in the old app's keychain entries, which a renamed app can't
/// read, so git asks for those again the first time they are needed.</item>
/// </list>
/// Nothing is deleted: the old copies stay until GitLab Desktop is uninstalled.
/// </summary>
static class LegacyMigration
{
    const string LegacyName = "GitLabDesktop";
    const string LegacyBundleId = "net.techidiots.gitlabdesktop";

    public static void Run()
    {
        try
        {
#if WINDOWS
            MigrateWindowsAppData();
            new LinkHandlerRegistration().MigrateLegacyRegistration();
#elif MACCATALYST
            MigrateMacPreferences();
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LegacyMigration] {ex.Message}");
        }
    }

#if WINDOWS
    static void MigrateWindowsAppData()
    {
        // An unpackaged app keeps its data in %LOCALAPPDATA%\<company>\<product>: Data (FileSystem.AppDataDirectory)
        // next to Settings (preferences.dat, securestorage.dat)
        var data = new DirectoryInfo(FileSystem.AppDataDirectory);
        if (data.Name != "Data" || data.Parent is not { } newRoot) return;
        var oldRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyName, LegacyName);
        if (!Directory.Exists(oldRoot) || string.Equals(Path.GetFullPath(oldRoot), newRoot.FullName, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(Path.Combine(newRoot.FullName, "Settings", "preferences.dat"))) return;   // CommitDesk's own already

        CopyTree(oldRoot, newRoot.FullName);
        Debug.WriteLine($"[LegacyMigration] copied {oldRoot} to {newRoot.FullName}");
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (!File.Exists(target)) File.Copy(file, target);
        }
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
#endif

#if MACCATALYST
    static void MigrateMacPreferences()
    {
        const string doneKey = "migrated_from_gitlab_desktop";
        var defaults = Foundation.NSUserDefaults.StandardUserDefaults;
        if (defaults.BoolForKey(doneKey)) return;
        // Only the old app's own domain, not the global settings every app sees
        if (defaults.PersistentDomainForName(LegacyBundleId) is { } old)
        {
            foreach (var (key, value) in old)
            {
                if (key.ToString() is { } name && defaults.ValueForKey(new Foundation.NSString(name)) is null)
                    defaults.SetValueForKey(value, new Foundation.NSString(name));
            }
        }
        defaults.SetBool(true, doneKey);
    }
#endif
}
