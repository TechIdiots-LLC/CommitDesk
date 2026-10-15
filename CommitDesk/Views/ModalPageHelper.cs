using CommitDesk.ViewModels;

namespace CommitDesk.Views;

static class ModalPageHelper
{
    /// <summary>Closes the page when the view model completes, and cancels the view model if the page is dismissed.</summary>
    public static void Attach<T>(ContentPage page, ModalViewModel<T> vm)
    {
        page.BindingContext = vm;
        vm.CloseRequested += async () =>
        {
            if (page.Navigation.ModalStack.Contains(page)) await page.Navigation.PopModalAsync();
        };
        page.Disappearing += (_, _) => vm.Cancel();
    }
}
