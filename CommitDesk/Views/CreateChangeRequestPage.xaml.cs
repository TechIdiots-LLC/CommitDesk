using CommitDesk.ViewModels;

namespace CommitDesk.Views;

public partial class CreateChangeRequestPage : ContentPage
{
    public CreateChangeRequestPage(CreateChangeRequestViewModel vm)
    {
        InitializeComponent();
        ModalPageHelper.Attach(this, vm);
    }
}
