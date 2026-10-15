using System.Diagnostics;
using System.Text;

namespace CommitDesk.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

public sealed class GitException(string message, GitResult result) : Exception(message)
{
    public GitResult Result { get; } = result;
}

/// <summary>
/// Runs the git command-line client. Repository operations shell out to git (as GitHub Desktop
/// does) so behaviour, hooks, credential helpers and config all match what the user gets in a terminal.
/// </summary>
public sealed class GitRunner
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Path to git.exe, or "git" to resolve from PATH.</summary>
    public string GitExecutable { get; set; } = "git";

    /// <summary>
    /// Extra git config passed through GIT_CONFIG_COUNT/KEY/VALUE environment variables, so secrets
    /// such as an access-token auth header never appear on the process command line.
    /// </summary>
    public Func<IReadOnlyList<KeyValuePair<string, string>>>? ConfigProvider { get; set; }

    /// <summary>Raised after every command, for the output log.</summary>
    public event Action<string, GitResult>? CommandCompleted;

    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        string? stdin = null,
        bool throwOnError = true,
        CancellationToken ct = default,
        Action<string>? progress = null)
    {
        var psi = CreateStartInfo(workingDirectory, args, stdin is not null);
        using var proc = Start(psi);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = progress is null ? proc.StandardError.ReadToEndAsync(ct) : ReadProgressAsync(proc.StandardError, progress, ct);
        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin.AsMemory(), ct);
            proc.StandardInput.Close();
        }

        await WaitAsync(proc, ct);

        var result = new GitResult(proc.ExitCode, await stdoutTask, await stderrTask);
        Report(psi, result);

        if (throwOnError && !result.Success)
        {
            var msg = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
            throw new GitException(msg.Trim(), result);
        }
        return result;
    }

    /// <summary>
    /// Reads git's standard error as it is written, passing each progress update to <paramref name="progress"/>. Git
    /// redraws a progress line in place with a carriage return, so every redraw is reported but only a line's last
    /// version (ended by a newline) is kept for the returned text, as a terminal would show it.
    /// </summary>
    static async Task<string> ReadProgressAsync(StreamReader stderr, Action<string> progress, CancellationToken ct)
    {
        var kept = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[1024];
        int n;
        while ((n = await stderr.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                var c = buffer[i];
                if (c is not ('\r' or '\n'))
                {
                    line.Append(c);
                    continue;
                }
                if (line.Length > 0) progress(line.ToString());
                if (c == '\n') kept.Append(line).Append('\n');
                line.Clear();
            }
        }
        if (line.Length > 0)
        {
            progress(line.ToString());
            kept.Append(line);
        }
        return kept.ToString();
    }

    /// <summary>
    /// Runs git and returns its standard output as raw bytes (e.g. <c>git cat-file blob</c> for an image), or null when
    /// git fails (such as the object not existing).
    /// </summary>
    public async Task<byte[]?> ReadBytesAsync(string workingDirectory, IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = CreateStartInfo(workingDirectory, args, redirectStdin: false);
        using var proc = Start(psi);

        using var stdout = new MemoryStream();
        var copyTask = proc.StandardOutput.BaseStream.CopyToAsync(stdout, ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await WaitAsync(proc, ct);
        await copyTask;

        var result = new GitResult(proc.ExitCode, $"({stdout.Length} bytes)", await stderrTask);
        Report(psi, result);
        return result.Success ? stdout.ToArray() : null;
    }

    void Report(ProcessStartInfo psi, GitResult result)
        => CommandCompleted?.Invoke("git " + string.Join(' ', psi.ArgumentList.Skip(4)), result);

    Process Start(ProcessStartInfo psi)
    {
        var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
            return proc;
        }
        catch (Exception ex)
        {
            proc.Dispose();
            throw new GitException($"Could not start git ({GitExecutable}): {ex.Message}. Is git installed?",
                new GitResult(-1, "", ex.Message));
        }
    }

    static async Task WaitAsync(Process proc, CancellationToken ct)
    {
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }

    ProcessStartInfo CreateStartInfo(string workingDirectory, IEnumerable<string> args, bool redirectStdin)
    {
        var psi = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStdin,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (redirectStdin)
            psi.StandardInputEncoding = Utf8NoBom;

        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("color.ui=never");
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        // Never prompt for credentials, in a terminal or in Git Credential Manager's window: the app asks for a sign-in
        // itself when git reports an authentication failure. Credentials GCM already stored are still used.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        var config = ConfigProvider?.Invoke();
        if (config is { Count: > 0 })
        {
            psi.Environment["GIT_CONFIG_COUNT"] = config.Count.ToString();
            for (int i = 0; i < config.Count; i++)
            {
                psi.Environment[$"GIT_CONFIG_KEY_{i}"] = config[i].Key;
                psi.Environment[$"GIT_CONFIG_VALUE_{i}"] = config[i].Value;
            }
        }
        return psi;
    }
}
