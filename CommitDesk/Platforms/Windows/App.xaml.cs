using CommitDesk.Services;
using Microsoft.UI.Xaml;

namespace CommitDesk.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	public App()
	{
		this.InitializeComponent();
		// WinUI failures surface as an opaque 0xc000027b crash; log the managed exception first.
		UnhandledException += (_, e) => CrashLog.Write("WinUI", e.Exception);
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
