using CommitDesk.ViewModels;

namespace CommitDesk.Views;

public partial class SettingsPage : ContentPage
{
    readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ModalPageHelper.Attach(this, vm);
    }

    // Coming back from Windows Settings (after choosing the app for GitHub's links) re-checks who opens them
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Window is { } window) window.Activated += OnWindowActivated;
    }

    protected override void OnDisappearing()
    {
        if (Window is { } window) window.Activated -= OnWindowActivated;
        base.OnDisappearing();
    }

    void OnWindowActivated(object? sender, EventArgs e) => _vm.UpdateLinkHandlerStatus();
}
