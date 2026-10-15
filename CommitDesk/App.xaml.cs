using CommitDesk.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CommitDesk;

public partial class App : Application
{
    readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var vm = _services.GetRequiredService<MainViewModel>();
        var window = new Window(_services.GetRequiredService<AppShell>())
        {
            Title = "CommitDesk",
            Width = 1400,
            Height = 900,
            MinimumWidth = 900,
            MinimumHeight = 600,
        };

#if WINDOWS
        WinUI.WindowPlacement.Attach(window);
        // A later launch (an "Open with GitHub Desktop" link, "CommitDesk.exe <folder>") comes to this window
        WinUI.Program.Relaunched += args => MainThread.BeginInvokeOnMainThread(async () =>
        {
            WinUI.WindowPlacement.BringToFront(window);
            await vm.HandleLaunchArgumentsAsync(args);
        });
#endif
        window.Created += async (_, _) => await vm.InitializeAsync();
        // Pick up changes made in other tools whenever the window regains focus.
        window.Activated += async (_, _) => await vm.RefreshAsync();
        return window;
    }
}
