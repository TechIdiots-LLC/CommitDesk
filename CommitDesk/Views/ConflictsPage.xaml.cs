using CommitDesk.ViewModels;

namespace CommitDesk.Views;

public partial class ConflictsPage : ContentPage
{
    readonly ConflictsViewModel _vm;

    public ConflictsPage(ConflictsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ModalPageHelper.Attach(this, vm);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartWatching(Dispatcher);
        _ = _vm.RecheckAsync();
    }

    protected override void OnDisappearing()
    {
        _vm.StopWatching();
        base.OnDisappearing();
    }
}
