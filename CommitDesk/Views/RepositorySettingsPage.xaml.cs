using CommitDesk.ViewModels;

namespace CommitDesk.Views;

public partial class RepositorySettingsPage : ContentPage
{
    readonly RepositorySettingsViewModel _vm;

    public RepositorySettingsPage(RepositorySettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ModalPageHelper.Attach(this, vm);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
