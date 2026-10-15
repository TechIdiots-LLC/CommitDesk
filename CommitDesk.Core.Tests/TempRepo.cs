using CommitDesk.Core.Git;

namespace CommitDesk.Core.Tests;

/// <summary>A throwaway git repository in the temp directory.</summary>
sealed class TempRepo : IDisposable
{
    public GitRunner Git { get; } = new();
    public string Dir { get; }
    public GitRepository Repo { get; }

    TempRepo(string dir)
    {
        Dir = dir;
        Repo = new GitRepository(Git, dir);
    }

    public static async Task<TempRepo> CreateAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gld-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var t = new TempRepo(dir);
        await GitRepository.InitAsync(t.Git, dir);
        await t.RunAsync("config", "user.name", "Test");
        await t.RunAsync("config", "user.email", "test@example.com");
        await t.RunAsync("config", "core.autocrlf", "false");
        return t;
    }

    public Task<GitResult> RunAsync(params string[] args) => Git.RunAsync(Dir, args);

    public void Write(string file, string content) => File.WriteAllText(Path.Combine(Dir, file), content);

    public string Read(string file) => File.ReadAllText(Path.Combine(Dir, file));

    public async Task<string> ShowHeadAsync(string file) => (await RunAsync("show", $"HEAD:{file}")).StdOut;

    public async Task CommitAllAsync(string message)
    {
        await RunAsync("add", "-A");
        await RunAsync("commit", "-m", message);
    }

    public async Task<FileDiff> DiffAsync(string file)
    {
        var status = await Repo.GetStatusAsync();
        var change = status.Files.Single(f => f.Path == file);
        return await Repo.GetWorkingDiffAsync(change, status.IsUnborn, initiallySelected: false);
    }

    public async Task CommitPartialAsync(string file, FileDiff diff, string message = "partial")
    {
        var status = await Repo.GetStatusAsync();
        var change = status.Files.Single(f => f.Path == file);
        await Repo.CommitAsync(message, [new CommitFileSelection(change, diff)], amend: false, status.IsUnborn);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Dir, recursive: true);
        }
        catch { }
    }
}
