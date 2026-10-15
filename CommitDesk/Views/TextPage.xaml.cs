namespace CommitDesk.Views;

/// <summary>Read-only text in a modal page (e.g. the git command log).</summary>
public partial class TextPage : ContentPage
{
    public TextPage(string title, string text)
    {
        InitializeComponent();
        TitleLabel.Text = title;
        Body.Text = text;
    }

    async void OnClose(object? sender, EventArgs e) => await Navigation.PopModalAsync();
}
