using CommitDesk.ViewModels;

namespace CommitDesk.Views;

public partial class GitSignInPage : ContentPage
{
    public GitSignInPage(GitSignInViewModel vm)
    {
        InitializeComponent();
        ModalPageHelper.Attach(this, vm);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SecretEntry.Focus();
    }
}
