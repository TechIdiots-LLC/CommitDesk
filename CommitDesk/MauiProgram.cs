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
        services.AddSingleton<GitRunner>();
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
}
