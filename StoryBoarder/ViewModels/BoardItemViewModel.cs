using System.Windows;
using System.Windows.Media;
using StoryBoarder.Models;
using StoryBoarder.Services;

namespace StoryBoarder.ViewModels;

public abstract class BoardItemViewModel : ObservableObject
{
    private double _x, _y, _width, _height;
    private string _text = "";
    private string _color = Palette.DefaultNote;
    private bool _isSelected, _isEditing, _isDropTarget;

    public abstract ItemKind Kind { get; }
    public abstract double MinSize { get; }

    public string Id { get; set; } = NewId();
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double Width { get => _width; set => Set(ref _width, value); }
    public double Height { get => _height; set => Set(ref _height, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }
    /// <summary>Highlighted while an arrow is being dragged over the item.</summary>
    public bool IsDropTarget { get => _isDropTarget; set => Set(ref _isDropTarget, value); }

    public Rect Bounds => new(X, Y, Width, Height);
    public Point Center => new(X + Width / 2, Y + Height / 2);

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public virtual BoardItemData ToData() => new()
    {
        Id = Id,
        Kind = Kind,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Text = Text,
        Color = Color,
    };

    public virtual void Apply(BoardItemData data)
    {
        Id = string.IsNullOrEmpty(data.Id) ? NewId() : data.Id;
        X = data.X;
        Y = data.Y;
        Width = data.Width;
        Height = data.Height;
        Text = data.Text;
        Color = data.Color;
    }

    public static BoardItemViewModel FromData(BoardItemData data)
    {
        BoardItemViewModel item = data.Kind switch
        {
            ItemKind.Frame => new FrameViewModel(),
            ItemKind.Text => new TextViewModel(),
            ItemKind.Image => new ImageViewModel(),
            _ => new NoteViewModel(),
        };
        item.Apply(data);
        return item;
    }
}

public sealed class NoteViewModel : BoardItemViewModel
{
    public override ItemKind Kind => ItemKind.Note;
    public override double MinSize => 60;
}

/// <summary>Free text with no background; the font grows and shrinks with the box.</summary>
public sealed class TextViewModel : BoardItemViewModel
{
    public override ItemKind Kind => ItemKind.Text;
    public override double MinSize => 30;
}

public sealed class ImageViewModel : BoardItemViewModel
{
    private string? _imageId;

    public override ItemKind Kind => ItemKind.Image;
    public override double MinSize => 40;

    public string? ImageId
    {
        get => _imageId;
        set
        {
            if (Set(ref _imageId, value)) OnPropertyChanged(nameof(Source));
        }
    }

    public ImageSource? Source => ImageStore.Get(ImageId);

    public override BoardItemData ToData()
    {
        var data = base.ToData();
        data.ImageId = ImageId;
        return data;
    }

    public override void Apply(BoardItemData data)
    {
        base.Apply(data);
        ImageId = data.ImageId;
    }
}

public sealed class FrameViewModel : BoardItemViewModel
{
    public override ItemKind Kind => ItemKind.Frame;
    public override double MinSize => 120;

    /// <summary>True if the item sits inside this frame, so it moves and gets deleted along with it.</summary>
    public bool Contains(BoardItemViewModel item) =>
        item != this && Bounds.Contains(item.Center) && item.Width * item.Height < Width * Height;
}
