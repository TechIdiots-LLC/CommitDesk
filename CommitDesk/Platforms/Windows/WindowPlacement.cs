using System.Text.Json;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace CommitDesk.WinUI;

/// <summary>
/// Remembers the main window's position, size and maximized state between runs. The first run, or a saved position
/// that is no longer on any display (monitor unplugged, resolution changed), centres the window on the primary display.
/// Bounds are stored in physical pixels, as the Windows windowing API reports them.
/// </summary>
static class WindowPlacement
{
    const string Key = "window_placement";

    sealed record Placement(int X, int Y, int Width, int Height, bool Maximized);

    /// <summary>Shows the window in front, restored if it was minimised (for a launch handed over to this copy).</summary>
    public static void BringToFront(Window window)
    {
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return;
        if (native.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        native.Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(native));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);

    public static void Attach(Window window)
    {
        bool attached = false;
        window.HandlerChanged += (_, _) =>
        {
            if (attached || window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native) return;
            attached = true;
            var appWindow = native.AppWindow;

            // The multi-size icon (16-256 px) instead of MAUI's single 64 px appicon.ico, for a crisp title bar and taskbar.
            var icon = Path.Combine(AppContext.BaseDirectory, "CommitDesk.ico");
            if (File.Exists(icon)) appWindow.SetIcon(icon);

            // The restored (not maximized/minimized) bounds, so un-maximizing next time returns to them. Taken from
            // Restore, not the window: a window restored as maximized already reports its maximized size.
            var normal = Restore(appWindow);
            appWindow.Changed += (_, e) =>
            {
                if ((e.DidPositionChange || e.DidSizeChange) &&
                    appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
                    normal = Bounds(appWindow);
            };

            void Save()
            {
                bool maximized = appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
                var placement = new Placement(normal.X, normal.Y, normal.Width, normal.Height, maximized);
                Preferences.Set(Key, JsonSerializer.Serialize(placement));
            }

            // Deactivated as well as Destroying, so the position survives the app being killed.
            window.Deactivated += (_, _) => Save();
            window.Destroying += (_, _) => Save();
        };
    }

    static RectInt32 Bounds(AppWindow w) => new(w.Position.X, w.Position.Y, w.Size.Width, w.Size.Height);

    /// <returns>The normal (un-maximized) bounds the window was given.</returns>
    static RectInt32 Restore(AppWindow appWindow)
    {
        Placement? saved = null;
        try { saved = JsonSerializer.Deserialize<Placement>(Preferences.Get(Key, "")); }
        catch (JsonException) { }

        if (saved is { Width: > 0, Height: > 0 })
        {
            var rect = new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
            // Only reuse it if the title bar area is still on some display.
            var titleBar = new RectInt32(saved.X, saved.Y, saved.Width, Math.Min(saved.Height, 40));
            if (DisplayArea.GetFromRect(titleBar, DisplayAreaFallback.None) is not null)
            {
                appWindow.MoveAndResize(rect);
                if (saved.Maximized && appWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
                return rect;
            }
        }

        // First run (or saved position off-screen): fit the default size into the work area and centre it.
        var work = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(appWindow.Size.Width, work.Width * 9 / 10);
        int height = Math.Min(appWindow.Size.Height, work.Height * 9 / 10);
        var centred = new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
        appWindow.MoveAndResize(centred);
        return centred;
    }
}
