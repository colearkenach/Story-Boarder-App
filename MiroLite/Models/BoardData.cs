namespace MiroLite.Models;

public enum ItemKind
{
    Note,
    Frame,
}

/// <summary>One sticky note or frame as it is stored in a .mlboard file.</summary>
public sealed class BoardItemData
{
    public ItemKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Text { get; set; } = "";
    public string Color { get; set; } = Palette.DefaultNote;
}

public sealed class BoardData
{
    public int Version { get; set; } = 1;
    public List<BoardItemData> Items { get; set; } = new();
}
