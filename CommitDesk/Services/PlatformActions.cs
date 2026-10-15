using System.Diagnostics;

namespace CommitDesk.Services;

/// <summary>Opening things outside the app: browser, file manager, terminal, editor.</summary>
public sealed class PlatformActions(AppSettings settings)
{
    readonly AppSettings _settings = settings;

    public Task OpenUrlAsync(string url) => Launcher.Default.OpenAsync(new Uri(url));

    public Task CopyAsync(string text) => Clipboard.Default.SetTextAsync(text);

    public void ShowInFileManager(string path)
    {
#if WINDOWS
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else
            Process.Start("explorer.exe", $"\"{path}\"");
#elif MACCATALYST
        // -R reveals a file in its Finder window; a folder is simply opened.
        Run("/usr/bin/open", File.Exists(path) ? ["-R", path] : [path]);
#else
        throw new PlatformNotSupportedException("Showing files is not supported on this platform.");
#endif
    }

    public void OpenTerminal(string directory)
    {
#if WINDOWS
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", ["-d", directory]) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = directory, UseShellExecute = true });
        }
#elif MACCATALYST
        Run("/usr/bin/open", ["-a", "Terminal", directory]);
#else
        throw new PlatformNotSupportedException("Opening a terminal is not supported on this platform.");
#endif
    }

    public void OpenInEditor(string path)
    {
#if WINDOWS
        // Run through cmd so .cmd shims such as VS Code's "code" resolve from PATH.
        var psi = new ProcessStartInfo("cmd.exe") { CreateNoWindow = true, UseShellExecute = false };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(_settings.EditorCommand);
        psi.ArgumentList.Add(path);
        Process.Start(psi);
#elif MACCATALYST
        // Apps started from Finder get a minimal PATH; a login shell picks up the user's (e.g. /opt/homebrew/bin,
        // or /usr/local/bin where VS Code installs "code"). The path is passed as $1, never spliced into the command.
        Run("/bin/zsh", ["-lc", $"{_settings.EditorCommand} \"$1\"", "zsh", path]);
#else
        throw new PlatformNotSupportedException("Opening an editor is not supported on this platform.");
#endif
    }

    public void OpenWithDefaultApp(string path)
    {
#if WINDOWS
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
#elif MACCATALYST
        Run("/usr/bin/open", [path]);
#else
        _ = Launcher.Default.OpenAsync(new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path)));
#endif
    }

#if MACCATALYST
    static void Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }
#endif
}
