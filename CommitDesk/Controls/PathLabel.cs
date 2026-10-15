namespace CommitDesk.Controls;

/// <summary>
/// A file path as folder (dimmed) plus file name. When the row is too narrow the
/// folder is shortened from the front ("…Core/GitHub/") so the file name stays whole; only a file name wider than
/// the row itself is cut, in the middle. Hovering shows the full path.
/// </summary>
public sealed class PathLabel : ContentView
{
    public static readonly BindableProperty DirectoryProperty =
        BindableProperty.Create(nameof(Directory), typeof(string), typeof(PathLabel), "", propertyChanged: (b, _, _) => ((PathLabel)b).Update());

    public static readonly BindableProperty FileNameProperty =
        BindableProperty.Create(nameof(FileName), typeof(string), typeof(PathLabel), "", propertyChanged: (b, _, _) => ((PathLabel)b).Update());

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(PathLabel), 13.0, propertyChanged: (b, _, n) =>
        {
            var p = (PathLabel)b;
            p._dir.FontSize = p._file.FontSize = (double)n;
            p.Fit();
        });

    public string Directory { get => (string)GetValue(DirectoryProperty); set => SetValue(DirectoryProperty, value); }
    public string FileName { get => (string)GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    readonly Label _dir = new() { LineBreakMode = LineBreakMode.HeadTruncation, MaxLines = 1, FontSize = 13 };
    readonly Label _file = new() { LineBreakMode = LineBreakMode.MiddleTruncation, MaxLines = 1, FontSize = 13 };

    public PathLabel()
    {
        _dir.SetAppThemeColor(Label.TextColorProperty,
            (Color)Application.Current!.Resources["TextSecondaryLight"], (Color)Application.Current.Resources["TextSecondaryDark"]);
        var grid = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto)], HorizontalOptions = LayoutOptions.Start };
        grid.Add(_dir, 0, 0);
        grid.Add(_file, 1, 0);
        Content = grid;
        SizeChanged += (_, _) => Fit();
    }

    void Update()
    {
        _dir.Text = Directory;
        _dir.IsVisible = !string.IsNullOrEmpty(Directory);
        _file.Text = FileName;
        ToolTipProperties.SetText(this, Directory + FileName);
        Fit();
    }

    /// <summary>The file name gets the width it needs (up to the whole row); the folder gets what's left.</summary>
    void Fit()
    {
        if (Width <= 0) return;
        _file.MaximumWidthRequest = double.PositiveInfinity;
        _dir.MaximumWidthRequest = double.PositiveInfinity;
        var fileWidth = Math.Min(_file.Measure(double.PositiveInfinity, double.PositiveInfinity).Width, Width);
        _file.MaximumWidthRequest = Width;
        _dir.MaximumWidthRequest = Math.Max(0, Width - fileWidth);
        _dir.IsVisible = !string.IsNullOrEmpty(Directory) && Width - fileWidth > 16;
    }
}
