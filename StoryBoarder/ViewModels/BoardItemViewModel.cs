using System.Windows;
using StoryBoarder.Models;

namespace StoryBoarder.ViewModels;

public abstract class BoardItemViewModel : ObservableObject
{
    private double _x, _y, _width, _height;
    private string _text = "";
    private string _color = Palette.DefaultNote;
    private bool _isSelected, _isEditing;

    public abstract ItemKind Kind { get; }
    public abstract double MinSize { get; }

    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double Width { get => _width; set => Set(ref _width, value); }
    public double Height { get => _height; set => Set(ref _height, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsEditing { get => _isEditing; set => Set(ref _isEditing, value); }

    public Rect Bounds => new(X, Y, Width, Height);
    public Point Center => new(X + Width / 2, Y + Height / 2);

    public BoardItemData ToData() => new()
    {
        Kind = Kind,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        Text = Text,
        Color = Color,
    };

    public void Apply(BoardItemData data)
    {
        X = data.X;
        Y = data.Y;
        Width = data.Width;
        Height = data.Height;
        Text = data.Text;
        Color = data.Color;
    }

    public static BoardItemViewModel FromData(BoardItemData data)
    {
        BoardItemViewModel item = data.Kind == ItemKind.Frame ? new FrameViewModel() : new NoteViewModel();
        item.Apply(data);
        return item;
    }
}

public sealed class NoteViewModel : BoardItemViewModel
{
    public override ItemKind Kind => ItemKind.Note;
    public override double MinSize => 60;
}

public sealed class FrameViewModel : BoardItemViewModel
{
    public override ItemKind Kind => ItemKind.Frame;
    public override double MinSize => 120;

    /// <summary>True if the item sits inside this frame, so it moves and gets deleted along with it.</summary>
    public bool Contains(BoardItemViewModel item) =>
        item != this && Bounds.Contains(item.Center) && item.Width * item.Height < Width * Height;
}
