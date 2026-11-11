using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using StoryBoarder.ViewModels;

namespace StoryBoarder.Controls;

public enum HandleKind
{
    /// <summary>The square at a selected item's bottom-right corner.</summary>
    Resize,
    /// <summary>The dots on a selected item's sides: drag one to draw an arrow.</summary>
    Connect,
}

/// <summary>
/// Draws selection outlines, resize and arrow handles, and the selection box on top of the board, in screen
/// coordinates, so they stay the same size at any zoom. Handles are bigger in touch mode, for fingers.
/// The board view hit-tests them with <see cref="HandleAt"/>.
/// </summary>
public sealed class SelectionAdorner : Control
{
    private static readonly ImmutableSolidColorBrush Accent = new(Color.Parse("#7048E8"));
    private static readonly IBrush BoxFill = new ImmutableSolidColorBrush(Color.Parse("#1A7048E8"));
    private static readonly IPen OutlinePen = new ImmutablePen(Accent, 2);
    private static readonly IPen BoxPen = new ImmutablePen(Accent, 1);

    public BoardViewModel? Board { get; set; }

    /// <summary>Board coordinates to screen coordinates.</summary>
    public Matrix View { get; set; } = Matrix.Identity;

    /// <summary>The selected item under the mouse, which shows its arrow dots (in touch mode they always show).</summary>
    public BoardItemViewModel? HoverItem { get; set; }

    public bool TouchMode { get; set; }

    /// <summary>The box being dragged out to select items, in screen coordinates.</summary>
    public Rect? SelectionBox { get; set; }

    private double ResizeSize => TouchMode ? 20 : 12;
    private double ResizeReach => TouchMode ? 24 : 11; // half the hit area's side
    private double DotRadius => TouchMode ? 10 : 7;
    private double DotReach => TouchMode ? 24 : 10;
    private double DotOffset => TouchMode ? 18 : 0;    // how far outside the item the dots sit

    public Rect ToScreen(Rect board) => board.TransformToAABB(View);

    /// <summary>The handle at <paramref name="point"/> (screen coordinates), top-most item first.</summary>
    public (BoardItemViewModel Item, HandleKind Kind)? HandleAt(Point point)
    {
        if (Board is null) return null;
        var selected = Board.SelectedItems.OrderByDescending(i => i.ZIndex).ToList();
        foreach (var item in selected)
        {
            var screen = ToScreen(item.Bounds);
            if (ShowsDots(item, selected.Count) && DotCenters(screen).Any(c => Point.Distance(c, point) <= DotReach))
                return (item, HandleKind.Connect);
            var corner = screen.BottomRight;
            if (Math.Abs(point.X - corner.X) <= ResizeReach && Math.Abs(point.Y - corner.Y) <= ResizeReach)
                return (item, HandleKind.Resize);
        }
        return null;
    }

    public override void Render(DrawingContext context)
    {
        if (Board is null) return;
        var items = Board.Items.OrderBy(i => i.ZIndex).ToList();
        var selectedCount = items.Count(i => i.IsSelected);

        foreach (var item in items.Where(i => i.IsSelected || i.IsDropTarget))
        {
            var screen = ToScreen(item.Bounds);
            var outline = item is FrameViewModel ? screen : screen.Inflate(3);
            context.DrawRectangle(null, OutlinePen, outline, 4, 4);
        }
        foreach (var item in items.Where(i => i.IsSelected))
        {
            var screen = ToScreen(item.Bounds);
            if (ShowsDots(item, selectedCount))
                foreach (var center in DotCenters(screen))
                    context.DrawEllipse(Brushes.White, OutlinePen, center, DotRadius, DotRadius);
            var size = ResizeSize;
            context.DrawRectangle(Brushes.White, OutlinePen,
                new Rect(screen.Right - size / 2, screen.Bottom - size / 2, size, size), 2, 2);
        }
        if (SelectionBox is { } box) context.DrawRectangle(BoxFill, BoxPen, box);
    }

    // Frames have no arrow dots; in touch mode they show when one item is selected, with the mouse on hover.
    private bool ShowsDots(BoardItemViewModel item, int selectedCount) =>
        item is not FrameViewModel && !item.IsEditing && (TouchMode ? selectedCount == 1 : item == HoverItem);

    private Point[] DotCenters(Rect screen) =>
    [
        new(screen.Center.X, screen.Top - DotOffset),
        new(screen.Right + DotOffset, screen.Center.Y),
        new(screen.Center.X, screen.Bottom + DotOffset),
        new(screen.Left - DotOffset, screen.Center.Y),
    ];
}
