using CommunityToolkit.Maui;
using CommitDesk.Core.Git;
using CommitDesk.Services;
using CommitDesk.ViewModels;
using CommitDesk.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CommitDesk;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        CrashLog.Register();
        // Settings from when the app was called GitLab Desktop, before anything reads one
        LegacyMigration.Run();
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;

        // ── Infrastructure ───────────────────────────────────────────────────
        services.AddSingleton<AppSettings>();
        services.AddSingleton(_ => CreateGitRunner());
        services.AddHttpClient("hosting", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("CommitDesk/0.1");
        });
        services.AddSingleton<HostingRegistry>();
        services.AddSingleton<GitSignInService>();
        services.AddSingleton<CertificateTrustService>();
        services.AddSingleton<AppUpdater>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<PlatformActions>();
        services.AddSingleton<LinkHandlerRegistration>();

        // ── ViewModels ───────────────────────────────────────────────────────
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<CloneViewModel>();

        // ── Shell + Pages ────────────────────────────────────────────────────
        services.AddSingleton<AppShell>();
        services.AddSingleton<MainPage>();

        return builder.Build();
    }

    /// <summary>
    /// Git through the git command line, or through libgit2 where there is none (Android and iOS). Setting the
    /// environment variable COMMITDESK_LIBGIT2=1 uses libgit2 on Windows and macOS too, to try the Android code paths.
    /// </summary>
    static GitRunner CreateGitRunner()
    {
        var useLibGit2 = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
            || Environment.GetEnvironmentVariable("COMMITDESK_LIBGIT2") == "1";
        // libgit2's user config (git's ~/.gitconfig): the app's own folder on a phone, which has no home directory
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            LibGit2Setup.Configure(Path.Combine(FileSystem.AppDataDirectory, "home"));
        return new GitRunner { UseLibGit2 = useLibGit2 };
    }
}
