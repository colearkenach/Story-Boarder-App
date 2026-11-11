using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace StoryBoarder.Controls;

public static class Converters
{
    private static readonly Dictionary<string, IBrush> Brushes = new();

    /// <summary>A "#RRGGBB" color (as stored on board items) as a brush.</summary>
    public static readonly IValueConverter HexBrush = new FuncValueConverter<string?, IBrush?>(BrushFor);

    /// <summary>Divides the first bound number by the second; no limit if the second isn't positive.</summary>
    public static readonly IMultiValueConverter Divide = new FuncMultiValueConverter<double, double>(values =>
        values.ToList() is [var value, > 0 and var divisor] ? value / divisor : double.PositiveInfinity);

    public static IBrush? BrushFor(string? hex)
    {
        if (hex is null) return null;
        if (Brushes.TryGetValue(hex, out var brush)) return brush;
        if (!Color.TryParse(hex, out var color)) return null;
        return Brushes[hex] = new ImmutableSolidColorBrush(color);
    }
}
