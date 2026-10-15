namespace CommitDesk.Services;

/// <summary>Appends unhandled exceptions to crash.log in the app data folder.</summary>
public static class CrashLog
{
    public static string FilePath { get; } = Path.Combine(FileSystem.AppDataDirectory, "crash.log");

    public static void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Task", e.Exception);
    }

    public static void Write(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(FilePath, $"==== {DateTime.Now:O} [{source}]\n{ex}\n\n");
        }
        catch { }
    }
}
