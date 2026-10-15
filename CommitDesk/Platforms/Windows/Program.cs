using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace CommitDesk.WinUI;

/// <summary>
/// The app's entry point on Windows (instead of the one WinUI generates; see DISABLE_XAML_GENERATED_MAIN in the
/// csproj), so only one copy runs: a second launch, such as a clicked "Open with GitHub Desktop" link or
/// "CommitDesk.exe &lt;folder&gt;", hands its arguments to the running app and exits.
/// </summary>
public static class Program
{
    /// <summary>Raised in the running app with each later launch's arguments (on a background thread).</summary>
    public static event Action<IReadOnlyList<string>>? Relaunched;

    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var current = AppInstance.FindOrRegisterForKey("CommitDesk");
        if (!current.IsCurrent)
        {
            // Another copy is running: let it come to the front (this process was just started by the user, so it may
            // grant that), and pass this launch on to it. The wait runs off the UI thread, which isn't pumping.
            AllowSetForegroundWindow(-1);   // ASFW_ANY
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => current.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        current.Activated += (_, e) => Relaunched?.Invoke(ArgumentsOf(e));
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>A redirected launch's command-line arguments, without the program itself.</summary>
    static IReadOnlyList<string> ArgumentsOf(AppActivationArguments e)
    {
        var commandLine = (e.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments;
        var args = Core.Hosting.RepositoryLink.SplitCommandLine(commandLine);
        // Unpackaged apps get the whole command line, program path first
        return args.Count > 0 && args[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? args.Skip(1).ToList() : args;
    }
}
