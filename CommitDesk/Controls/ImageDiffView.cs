using CommitDesk.Core.Git;
using Microsoft.Maui.Controls.Shapes;

namespace CommitDesk.Controls;

/// <summary>
/// Shows an image change the way GitHub Desktop does: the new image framed in green ("Added"), the old one in red
/// ("Deleted"), or both side by side when the file changed. Images sit on a checkerboard so transparency shows, and are
/// scaled down to fit but never enlarged.
/// </summary>
public sealed class ImageDiffView : ContentView
{
    public static readonly BindableProperty DiffProperty =
        BindableProperty.Create(nameof(Diff), typeof(ImageDiff), typeof(ImageDiffView),
            propertyChanged: (b, _, _) => ((ImageDiffView)b).Build());

    public ImageDiff? Diff
    {
        get => (ImageDiff?)GetValue(DiffProperty);
        set => SetValue(DiffProperty, value);
    }

    static readonly Color Red = Color.FromArgb("#CF222E");
    static readonly Color Green = Color.FromArgb("#2DA44E");

    // Each shown image's frame and natural pixel size, re-fitted whenever the view is resized
    readonly List<(Grid Box, ImageVersion Version)> _boxes = [];

    public ImageDiffView()
    {
        SizeChanged += (_, _) => Fit();
    }

    void Build()
    {
        _boxes.Clear();
        if (Diff is not { } diff)
        {
            Content = null;
            return;
        }

        var sides = new List<View>();
        if (diff.Old is { } old && diff.New is { } @new)
        {
            sides.Add(Side("Before", Red, old));
            sides.Add(Side("After", Green, @new));
        }
        else if (diff.Old is { } deleted)
        {
            sides.Add(Side("Deleted", Red, deleted));
        }
        else if (diff.New is { } added)
        {
            sides.Add(Side("Added", Green, added));
        }

        if (sides.Count == 0)
        {
            Content = new Label
            {
                Text = $"The image is larger than {ImageDiff.MaxPreviewBytes / (1024 * 1024)} MB, so it isn't previewed.",
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            return;
        }

        var grid = new Grid { Padding = new Thickness(24), ColumnSpacing = 24 };
        for (int i = 0; i < sides.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.Add(sides[i], i, 0);
        }
        Content = grid;
        Fit();
    }

    View Side(string title, Color color, ImageVersion version)
    {
        var image = new Image
        {
            Source = ImageSource.FromStream(() => new MemoryStream(version.DisplayBytes)),
            Aspect = Aspect.AspectFit,
        };
        var box = new Grid { HorizontalOptions = LayoutOptions.Center };
        box.Add(new GraphicsView { Drawable = Checkerboard.Instance });
        box.Add(image);
        _boxes.Add((box, version));

        return new VerticalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = title, TextColor = color, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center },
                new Border
                {
                    Stroke = color,
                    StrokeThickness = 1,
                    StrokeShape = new Rectangle(),
                    Padding = 0,
                    HorizontalOptions = LayoutOptions.Center,
                    Content = box,
                },
                new Label
                {
                    Text = version.Caption,
                    FontSize = 12,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#8B949E") : Color.FromArgb("#57606A"),
                },
            },
        };
    }

    /// <summary>Sizes each image to its pixel size, shrunk to fit its column and the view's height.</summary>
    void Fit()
    {
        if (_boxes.Count == 0 || Width <= 0 || Height <= 0) return;
        double maxWidth = (Width - 48 - 24 * (_boxes.Count - 1)) / _boxes.Count;
        double maxHeight = Height - 48 - 70;   // padding, title and caption
        foreach (var (box, version) in _boxes)
        {
            // Without a size in the header, assume a typical icon and let AspectFit do the rest.
            double w = version.Width ?? 256, h = version.Height ?? 256;
            double scale = Math.Min(1, Math.Min(maxWidth / w, maxHeight / h));
            if (scale <= 0) continue;
            box.WidthRequest = Math.Max(1, w * scale);
            box.HeightRequest = Math.Max(1, h * scale);
        }
    }

    /// <summary>The light grey checkerboard image editors use to show transparency.</summary>
    sealed class Checkerboard : IDrawable
    {
        public static readonly Checkerboard Instance = new();
        const float Cell = 8;

        public void Draw(ICanvas canvas, RectF rect)
        {
            canvas.FillColor = Colors.White;
            canvas.FillRectangle(rect);
            canvas.FillColor = Color.FromArgb("#E6E6E6");
            for (float y = 0; y < rect.Height; y += Cell)
            for (float x = (y / Cell) % 2 == 0 ? 0 : Cell; x < rect.Width; x += 2 * Cell)
                canvas.FillRectangle(x, y, Math.Min(Cell, rect.Width - x), Math.Min(Cell, rect.Height - y));
        }
    }
}
