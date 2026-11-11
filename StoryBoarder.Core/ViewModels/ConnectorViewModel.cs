using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using StoryBoarder.Models;

namespace StoryBoarder.ViewModels;

/// <summary>An arrow between two items. It follows them as they move or resize.</summary>
public sealed class ConnectorViewModel : BoardElementViewModel
{
    public const double LineWidth = 2.5;

    private bool _isSelected;
    private double _scale;
    private Geometry? _geometry;

    public ConnectorViewModel(BoardItemViewModel from, BoardItemViewModel to, double scale)
    {
        From = from;
        To = to;
        _scale = scale;
        from.PropertyChanged += OnEndChanged;
        to.PropertyChanged += OnEndChanged;
        Update();
    }

    public BoardItemViewModel From { get; private set; }
    public BoardItemViewModel To { get; private set; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public Geometry? Geometry { get => _geometry; private set => Set(ref _geometry, value); }
    public double Thickness => LineWidth * _scale;
    public double HitThickness => 16 * _scale; // a wider invisible line, so the arrow is easy to click

    /// <summary>Thickens the arrow (and its head) so it stays visible when zoomed out.</summary>
    public double Scale
    {
        set
        {
            _scale = value;
            Update();
            OnPropertyChanged(nameof(Thickness));
            OnPropertyChanged(nameof(HitThickness));
        }
    }

    public ConnectorData ToData() => new() { From = From.Id, To = To.Id };

    public void Reverse()
    {
        (From, To) = (To, From);
        Update();
    }

    /// <summary>Stops following the two items; call when the arrow leaves the board.</summary>
    public void Detach()
    {
        From.PropertyChanged -= OnEndChanged;
        To.PropertyChanged -= OnEndChanged;
    }

    private void OnEndChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(X) or nameof(Y)
            or nameof(BoardItemViewModel.Width) or nameof(BoardItemViewModel.Height))
            Update();
    }

    private void Update() => Geometry = Build(From.Bounds, To.Bounds, _scale);

    /// <summary>
    /// A straight arrow from the edge of <paramref name="from"/> to the edge of <paramref name="to"/>, aimed
    /// center to center. Pass an empty-sized rect as <paramref name="to"/> to point at a spot.
    /// </summary>
    public static Geometry? Build(Rect from, Rect to, double scale)
    {
        var a = from.Center;
        var b = to.Center;
        var direction = new Vector(b.X - a.X, b.Y - a.Y);
        if (direction.Length < 1) return null;
        direction = direction.Normalize();

        var gap = 6 * scale;
        var headLength = 14 * scale;
        var start = Edge(from, direction) + direction * gap;
        var end = Edge(to, -direction) - direction * gap;
        // Items that touch or overlap still get a short arrow.
        if (Vector.Dot(end - start, direction) < headLength * 1.5) start = end - direction * (headLength * 1.5);

        var headBase = end - direction * headLength;
        var side = new Vector(-direction.Y, direction.X) * (headLength * 0.45);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false);
            context.LineTo(headBase);
            context.EndFigure(isClosed: false);
            context.BeginFigure(end, isFilled: true);
            context.LineTo(headBase + side);
            context.LineTo(headBase - side);
            context.EndFigure(isClosed: true);
        }
        return geometry;
    }

    // Where a ray from the middle of the rect, heading along `direction`, crosses its edge.
    private static Point Edge(Rect r, Vector direction)
    {
        var toSide = direction.X == 0 ? double.PositiveInfinity : r.Width / 2 / Math.Abs(direction.X);
        var toTopOrBottom = direction.Y == 0 ? double.PositiveInfinity : r.Height / 2 / Math.Abs(direction.Y);
        return r.Center + direction * Math.Min(toSide, toTopOrBottom);
    }
}
