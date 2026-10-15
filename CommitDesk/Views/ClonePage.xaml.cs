using CommitDesk.ViewModels;

namespace CommitDesk.Views;

public partial class ClonePage : ContentPage
{
    readonly CloneViewModel _vm;

    public ClonePage(CloneViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ModalPageHelper.Attach(this, vm);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.InitializeAsync();
    }
}
