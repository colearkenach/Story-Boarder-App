namespace StoryBoarder.Models;

public enum ItemKind
{
    Note,
    Frame,
    Text,
    Image,
}

/// <summary>One board item as it is stored in a .storyboard file.</summary>
public sealed class BoardItemData
{
    /// <summary>Lets connectors refer to the item. Older files have none; the board assigns them on load.</summary>
    public string? Id { get; set; }
    public ItemKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Text { get; set; } = "";
    public string Color { get; set; } = Palette.DefaultNote;
    /// <summary>For images: the key of the picture in <see cref="BoardData.Images"/>.</summary>
    public string? ImageId { get; set; }
}

/// <summary>An arrow from one item to another, by item id.</summary>
public sealed class ConnectorData
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
}

public sealed class BoardData
{
    public int Version { get; set; } = 2;
    public List<BoardItemData> Items { get; set; } = new();
    public List<ConnectorData> Connectors { get; set; } = new();
    /// <summary>Image files by id, base64-encoded. Left out of undo snapshots to keep them small.</summary>
    public Dictionary<string, string>? Images { get; set; }
}
