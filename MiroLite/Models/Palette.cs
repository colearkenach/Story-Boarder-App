namespace MiroLite.Models;

public sealed record PaletteColor(string Name, string Hex);

/// <summary>Miro's sticky-note colors.</summary>
public static class Palette
{
    public const string Frame = "#FFFFFF";

    /// <summary>Colors offered in the toolbar and context menu; the first one is the default.</summary>
    public static readonly PaletteColor[] Notes =
    {
        new("Light yellow", "#FFF9B1"),
        new("Yellow", "#F5D128"),
        new("Orange", "#FF9D48"),
        new("Light green", "#D5F692"),
        new("Light blue", "#A6CCF5"),
        new("Light pink", "#FFCEE0"),
        new("Violet", "#C6A2D2"),
        new("Gray", "#E6E6E6"),
    };

    /// <summary>Colors the CSV importer hands out to groups of related notes.</summary>
    public static readonly string[] Accents = { "#A6CCF5", "#D5F692", "#FFCEE0", "#C6A2D2", "#FF9D48" };

    public static string DefaultNote => Notes[0].Hex;
}
