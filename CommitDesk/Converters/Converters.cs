using System.Globalization;
using CommitDesk.Core.Git;

namespace CommitDesk.Converters;

static class ThemeColors
{
    public static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;
    public static Color Hex(string light, string dark) => Color.FromArgb(IsDark ? dark : light);
}

public sealed class IsNotNullOrEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null => false,
            string s => s.Length > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Background of a diff row.</summary>
public sealed class DiffLineBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            DiffLineKind.Add => ThemeColors.Hex("#E6FFEC", "#12261E"),
            DiffLineKind.Delete => ThemeColors.Hex("#FFEBE9", "#25171C"),
            DiffLineKind.Hunk => ThemeColors.Hex("#DDF4FF", "#121D2F"),
            _ => Colors.Transparent,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Gutter colour for a diff row: solid green/red when the line is included in the commit,
/// a faint tint when it is not, nothing for context lines or read-only diffs.
/// Values: Kind, IsSelected, IsSelectable.
/// </summary>
public sealed class DiffGutterConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not DiffLineKind kind || values[2] is not true)
            return Colors.Transparent;
        bool selected = values[1] is true;
        return kind switch
        {
            DiffLineKind.Add => selected ? ThemeColors.Hex("#2DA44E", "#2EA043") : ThemeColors.Hex("#CCFFD8", "#1B3A2A"),
            DiffLineKind.Delete => selected ? ThemeColors.Hex("#CF222E", "#DA3633") : ThemeColors.Hex("#FFD7D5", "#3D1F24"),
            DiffLineKind.Hunk => ThemeColors.Hex("#B6E3FF", "#1F3A5F"),
            _ => Colors.Transparent,
        };
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DiffLineTextColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DiffLineKind.Hunk or DiffLineKind.NoNewline
            ? ThemeColors.Hex("#57606A", "#8B949E")
            : ThemeColors.Hex("#1F2328", "#E6EDF3");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FileChangeKindColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Color.FromArgb(value switch
        {
            FileChangeKind.Added or FileChangeKind.Untracked => "#2DA44E",
            FileChangeKind.Deleted => "#CF222E",
            FileChangeKind.Renamed or FileChangeKind.Copied => "#1F75CB",
            FileChangeKind.Conflicted => "#BF3989",
            _ => "#D29922",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset d ? Format(d) : "";

    public static string Format(DateTimeOffset d)
    {
        var span = DateTimeOffset.Now - d;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return Plural((int)span.TotalMinutes, "minute");
        if (span.TotalHours < 24) return Plural((int)span.TotalHours, "hour");
        if (span.TotalDays < 30) return Plural((int)span.TotalDays, "day");
        if (span.TotalDays < 365) return Plural((int)(span.TotalDays / 30), "month");
        return Plural((int)(span.TotalDays / 365), "year");
    }

    static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Colour for a GitLab pipeline status string.</summary>
public sealed class PipelineStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Color.FromArgb((value as string) switch
        {
            "success" => "#2DA44E",
            "failed" => "#DA3633",
            "running" or "pending" or "preparing" or "waiting_for_resource" => "#1F75CB",
            "canceled" or "skipped" => "#8B949E",
            _ => "#D29922",
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PipelineStatusGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "success" => "✓",
            "failed" => "✗",
            "running" => "●",
            "canceled" or "skipped" => "⊘",
            null => "",
            _ => "◌",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>✓ for a resolved conflicted file, ! for one that still has conflicts.</summary>
public sealed class ConflictGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "✓" : "!";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Green for a resolved conflicted file, the conflict colour otherwise.</summary>
public sealed class ConflictColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Color.FromArgb(value is true ? "#2DA44E" : "#BF3989");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
