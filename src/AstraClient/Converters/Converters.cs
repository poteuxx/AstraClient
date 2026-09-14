using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AstraClient.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool x && x;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible != Invert;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>Formats large counts compactly: 163886289 -> "163.9M", 42100 -> "42.1K".</summary>
public class CompactNumberConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double n = value switch
        {
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            _ => 0
        };
        string suffix = parameter as string ?? string.Empty;
        if (n >= 1_000_000_000) return $"{n / 1_000_000_000:0.#}B{suffix}";
        if (n >= 1_000_000) return $"{n / 1_000_000:0.#}M{suffix}";
        if (n >= 1_000) return $"{n / 1_000:0.#}K{suffix}";
        return $"{n:0}{suffix}";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Renders a DateTime as a friendly relative string ("3 days ago").</summary>
public class RelativeDateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime dt || dt == default)
            return string.Empty;
        var span = DateTime.UtcNow - dt.ToUniversalTime();
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} hr ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} days ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} mo ago";
        return $"{(int)(span.TotalDays / 365)} yr ago";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Null/empty string -> placeholder text; also used to collapse when empty.</summary>
public class NullToPlaceholderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string placeholder = parameter as string ?? "—";
        if (value is null) return placeholder;
        string s = value.ToString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(s) ? placeholder : s;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible when the bound string is null/empty (used for watermark text).</summary>
public class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts an int ARGB color (Modrinth "color") to a Brush; null -> transparent.</summary>
public class IntColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i && i != 0)
        {
            byte a = (byte)((i >> 24) & 0xFF);
            byte r = (byte)((i >> 16) & 0xFF);
            byte g = (byte)((i >> 8) & 0xFF);
            byte b = (byte)(i & 0xFF);
            if (a == 0) a = 255;
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
        return Brushes.Transparent;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Multiplies a 0..1 double by a pixel width (for progress fills bound to a ratio).</summary>
public class PercentToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double pct = value is double d ? d : 0;
        double total = parameter is string ps && double.TryParse(ps, out var p) ? p : 100;
        return new GridLength(Math.Max(0, Math.Min(1, pct)) * total);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Multi-value converter: returns true when both string bindings are equal (case-sensitive).</summary>
public class StringEqualityConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length != 2) return false;
        return values[0]?.ToString() == values[1]?.ToString();
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
