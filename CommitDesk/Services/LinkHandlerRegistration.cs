using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CommitDesk.Services;

/// <summary>
/// Lets GitHub's "Open with GitHub Desktop" links (x-github-client:) open in this app, without taking them from another
/// app behind the user's back. Windows only lets the user set a link type's default (the choice is protected), so
/// the app registers itself as able to open them and sends the user to Settings to choose; only when no other app
/// handles them does it register as the handler directly. Everything is under HKEY_CURRENT_USER, and removing it only
/// deletes what this app wrote. The app's own commitdesk: links are registered along the way.
/// </summary>
public sealed class LinkHandlerRegistration
{
    public const string GitHubScheme = "x-github-client";
    const string OwnScheme = "commitdesk";
    const string ProgId = "CommitDesk.Link";
    const string AppName = "CommitDesk";
    const string CapabilitiesKey = @"Software\TechIdiots\CommitDesk\Capabilities";
    const string OwnedMarker = "CommitDesk";   // value on a scheme key this app created, so it may delete it

    public static bool IsSupported => OperatingSystem.IsWindows();

    public enum Handler { None, ThisApp, OtherApp }

    /// <summary>Which app opens GitHub's links now, and that app's name when it isn't this one.</summary>
    public (Handler Handler, string? OtherAppName) CurrentHandler()
    {
#if WINDOWS
        using var choice = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{GitHubScheme}\UserChoice");
        if (choice?.GetValue("ProgId") is string chosen && chosen.Length > 0)
            return chosen == ProgId ? (Handler.ThisApp, null) : (Handler.OtherApp, AppNameOf(CommandOf(chosen)) ?? chosen);

        var command = CommandOf(GitHubScheme);
        if (command is null) return (Handler.None, null);
        return IsThisApp(command) ? (Handler.ThisApp, null) : (Handler.OtherApp, AppNameOf(command));
#else
        return (Handler.None, null);
#endif
    }

    /// <summary>Whether this app is registered as able to open the links (whether or not it's the default).</summary>
    public bool IsRegistered()
    {
#if WINDOWS
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}");
        return key is not null;
#else
        return false;
#endif
    }

    /// <summary>
    /// Registers the app for the links. Returns true when that already makes it the handler (no other app had them);
    /// otherwise the user picks it in Settings (<see cref="OpenDefaultAppSettings"/>).
    /// </summary>
    public bool Register()
    {
#if WINDOWS
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where the app is installed.");
        var hkcu = Microsoft.Win32.Registry.CurrentUser;

        void WriteUrlClass(string keyPath, string description, bool owned)
        {
            using var key = hkcu.CreateSubKey(keyPath);
            key.SetValue("", description);
            key.SetValue("URL Protocol", "");
            if (owned) key.SetValue(OwnedMarker, "1");
            using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{exe}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", $"\"{exe}\" \"%1\"");
        }

        // The ProgID Windows Settings offers, and the app's own scheme
        WriteUrlClass($@"Software\Classes\{ProgId}", "CommitDesk link", owned: false);
        WriteUrlClass($@"Software\Classes\{OwnScheme}", "URL:CommitDesk link", owned: true);

        // "This app can open these link types": what makes it a choice in Settings › Default apps
        using (var caps = hkcu.CreateSubKey(CapabilitiesKey))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "Open GitHub's \"Open with GitHub Desktop\" links in CommitDesk.");
            using var urls = caps.CreateSubKey("URLAssociations");
            urls.SetValue(GitHubScheme, ProgId);
            urls.SetValue("github-windows", ProgId);
            urls.SetValue(OwnScheme, ProgId);
        }
        using (var apps = hkcu.CreateSubKey(@"Software\RegisteredApplications"))
            apps.SetValue(AppName, CapabilitiesKey);

        // Nobody else opens GitHub's links: then there's nothing to take, so become their handler outright
        var becameHandler = false;
        if (CommandOf(GitHubScheme) is null)
        {
            WriteUrlClass($@"Software\Classes\{GitHubScheme}", "URL:GitHub Desktop link", owned: true);
            becameHandler = true;
        }
        NotifyShell();
        return becameHandler || CurrentHandler().Handler == Handler.ThisApp;
#else
        return false;
#endif
    }

    /// <summary>Removes everything this app registered; another app's registration is never touched.</summary>
    public void Unregister()
    {
#if WINDOWS
        var hkcu = Microsoft.Win32.Registry.CurrentUser;
        // The user's choice of this app in Settings would point at nothing; clear it so the other app's handler (e.g.
        // GitHub Desktop's) applies again. Only ever a choice of this app: Windows protects the value but allows this.
        var choicePath = $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{GitHubScheme}\UserChoice";
        bool choseThisApp;
        using (var choice = hkcu.OpenSubKey(choicePath)) choseThisApp = choice?.GetValue("ProgId") as string == ProgId;
        if (choseThisApp)
        {
            try { hkcu.DeleteSubKeyTree(choicePath, throwOnMissingSubKey: false); }
            catch (Exception) { /* left for Windows to ask again */ }
        }
        hkcu.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
        foreach (var scheme in new[] { OwnScheme, GitHubScheme })
        {
            bool owned;
            using (var key = hkcu.OpenSubKey($@"Software\Classes\{scheme}")) owned = key?.GetValue(OwnedMarker) is not null;
            if (owned) hkcu.DeleteSubKeyTree($@"Software\Classes\{scheme}", throwOnMissingSubKey: false);
        }
        hkcu.DeleteSubKeyTree(CapabilitiesKey, throwOnMissingSubKey: false);
        using (var apps = hkcu.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            apps?.DeleteValue(AppName, throwOnMissingValue: false);
        NotifyShell();
#endif
    }

    /// <summary>
    /// The app was called GitLab Desktop up to 0.6.x and registered under that name. Removes those registrations (the
    /// installer's takeover does this too, but not for a copy run from the zip) and, when the old app was registered for
    /// the links, registers this one in its place. A choice of the old app in Settings can't be carried over: Windows
    /// only lets the user make it, so it is cleared and the user picks CommitDesk again.
    /// </summary>
    public void MigrateLegacyRegistration()
    {
#if WINDOWS
        const string legacyProgId = "GitLabDesktop.Link", legacyName = "GitLab Desktop", legacyMarker = "GitLabDesktop";
        var hkcu = Microsoft.Win32.Registry.CurrentUser;
        bool wasRegistered;
        using (var key = hkcu.OpenSubKey($@"Software\Classes\{legacyProgId}")) wasRegistered = key is not null;
        if (!wasRegistered) return;

        try
        {
            var choicePath = $@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{GitHubScheme}\UserChoice";
            bool choseLegacy;
            using (var choice = hkcu.OpenSubKey(choicePath)) choseLegacy = choice?.GetValue("ProgId") as string == legacyProgId;
            if (choseLegacy) hkcu.DeleteSubKeyTree(choicePath, throwOnMissingSubKey: false);
            hkcu.DeleteSubKeyTree($@"Software\Classes\{legacyProgId}", throwOnMissingSubKey: false);
            foreach (var scheme in new[] { "gitlab-desktop", GitHubScheme })
            {
                bool owned;
                using (var key = hkcu.OpenSubKey($@"Software\Classes\{scheme}")) owned = key?.GetValue(legacyMarker) is not null;
                if (owned) hkcu.DeleteSubKeyTree($@"Software\Classes\{scheme}", throwOnMissingSubKey: false);
            }
            hkcu.DeleteSubKeyTree($@"Software\TechIdiots\{legacyName}", throwOnMissingSubKey: false);
            using (var apps = hkcu.OpenSubKey(@"Software\RegisteredApplications", writable: true))
                apps?.DeleteValue(legacyName, throwOnMissingValue: false);
            Register();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LinkHandlerRegistration] legacy migration failed: {ex.Message}");
        }
#endif
    }

    /// <summary>Windows Settings at this app's default-apps page, where the user can choose it for the links.</summary>
    public void OpenDefaultAppSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={Uri.EscapeDataString(AppName)}") { UseShellExecute = true });
        }
        catch (Exception)
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
    }

#if WINDOWS
    /// <summary>The open command registered for a scheme or ProgID (current user's first, then the machine's).</summary>
    static string? CommandOf(string classKey)
    {
        using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{classKey}\shell\open\command");
        return key?.GetValue("") as string is { Length: > 0 } command ? command : null;
    }

    static bool IsThisApp(string command)
        => Environment.ProcessPath is { } exe && command.Contains(exe, StringComparison.OrdinalIgnoreCase);

    /// <summary>"GitHub Desktop" for GitHub Desktop's launcher, else the program's file name.</summary>
    static string? AppNameOf(string? command)
    {
        if (command is null) return null;
        if (IsThisApp(command)) return AppName;
        if (command.Contains("GitHubDesktop", StringComparison.OrdinalIgnoreCase)) return "GitHub Desktop";
        var exe = command.TrimStart().StartsWith('"') ? command.TrimStart()[1..].Split('"')[0] : command.Split(' ')[0];
        return Path.GetFileNameWithoutExtension(exe);
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    // SHCNE_ASSOCCHANGED: Explorer and Settings re-read the registrations
    static void NotifyShell() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
#endif
}
