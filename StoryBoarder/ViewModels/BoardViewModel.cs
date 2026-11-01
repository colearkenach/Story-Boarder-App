using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using StoryBoarder.Models;
using StoryBoarder.Services;

namespace StoryBoarder.ViewModels;

/// <summary>
/// The board's items and arrows plus selection, moving and undo/redo. Undo works on whole-board
/// snapshots: take <see cref="Snapshot"/> before a change, then <see cref="Commit"/> it.
/// </summary>
public sealed class BoardViewModel : ObservableObject
{
    private const int MaxHistory = 200;

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private List<BoardItemViewModel> _moving = new();
    private bool _isDirty;
    private double _lineScale = 1;

    public ObservableCollection<BoardItemViewModel> Items { get; } = new();
    public ObservableCollection<ConnectorViewModel> Connectors { get; } = new();

    public IEnumerable<BoardItemViewModel> SelectedItems => Items.Where(i => i.IsSelected);

    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>How much arrows are thickened so they stay visible when zoomed out.</summary>
    public double LineScale
    {
        get => _lineScale;
        set
        {
            _lineScale = value;
            foreach (var connector in Connectors) connector.Scale = value;
        }
    }

    public void Load(BoardData data)
    {
        var imageIds = AddImages(data.Images);
        Items.Clear();
        var ids = new HashSet<string>();
        foreach (var itemData in data.Items)
        {
            var item = BoardItemViewModel.FromData(itemData);
            if (!ids.Add(item.Id)) item.Id = BoardItemViewModel.NewId();
            RemapImage(item, imageIds);
            Items.Add(item);
        }
        SetConnectors(data.Connectors);
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
        OnHistoryChanged();
    }

    /// <summary>The whole board, including the picture files, ready to save.</summary>
    public BoardData ToData()
    {
        var data = ContentData(Items);
        data.Images = ImagesFor(data.Items);
        return data;
    }

    public Rect? Extent()
    {
        if (Items.Count == 0) return null;
        var extent = Rect.Empty;
        foreach (var item in Items) extent.Union(item.Bounds);
        return extent;
    }

    // Items and the arrows between them, without picture files.
    private BoardData ContentData(IReadOnlyCollection<BoardItemViewModel> items)
    {
        var included = items.ToHashSet();
        return new BoardData
        {
            Items = items.Select(i => i.ToData()).ToList(),
            Connectors = Connectors.Where(c => included.Contains(c.From) && included.Contains(c.To))
                .Select(c => c.ToData()).ToList(),
        };
    }

    private static Dictionary<string, string>? ImagesFor(IEnumerable<BoardItemData> items)
    {
        var images = new Dictionary<string, string>();
        foreach (var id in items.Select(i => i.ImageId).OfType<string>().Distinct())
            if (ImageStore.GetBytes(id) is { } bytes) images[id] = Convert.ToBase64String(bytes);
        return images.Count > 0 ? images : null;
    }

    // Puts picture files into the image store and maps each file id to its store id
    // (the same, unless the file was edited by hand).
    private static Dictionary<string, string> AddImages(Dictionary<string, string>? images)
    {
        var ids = new Dictionary<string, string>();
        foreach (var (id, base64) in images ?? new())
        {
            try
            {
                ids[id] = ImageStore.Add(Convert.FromBase64String(base64));
            }
            catch (Exception e) when (e is FormatException or NotSupportedException or IOException or ArgumentException)
            {
                // An unreadable picture leaves an empty placeholder rather than failing the whole board.
            }
        }
        return ids;
    }

    private static void RemapImage(BoardItemViewModel item, Dictionary<string, string> imageIds)
    {
        if (item is ImageViewModel { ImageId: { } id } image && imageIds.TryGetValue(id, out var mapped))
            image.ImageId = mapped;
    }

    private void SetConnectors(IEnumerable<ConnectorData> connectors)
    {
        foreach (var connector in Connectors) connector.Detach();
        Connectors.Clear();
        var byId = new Dictionary<string, BoardItemViewModel>();
        foreach (var item in Items) byId.TryAdd(item.Id, item);
        foreach (var c in connectors)
            if (byId.TryGetValue(c.From, out var from) && byId.TryGetValue(c.To, out var to))
                Connect(from, to);
    }

    // ---------- Undo / redo ----------

    public string Snapshot() => BoardFile.Serialize(ContentData(Items));

    public void Commit(string before)
    {
        if (before == Snapshot()) return;
        Push(_undo, before);
        _redo.Clear();
        IsDirty = true;
        OnHistoryChanged();
    }

    public void Change(Action action)
    {
        var before = Snapshot();
        action();
        Commit(before);
    }

    public void Undo() => Step(_undo, _redo);
    public void Redo() => Step(_redo, _undo);

    private void Step(List<string> from, List<string> to)
    {
        if (from.Count == 0) return;
        Push(to, Snapshot());
        var state = BoardFile.Deserialize(from[^1]);
        from.RemoveAt(from.Count - 1);
        Restore(state);
        IsDirty = true;
        OnHistoryChanged();
    }

    // Updates items in place when the shape of the board hasn't changed, which keeps undoing a drag cheap.
    private void Restore(BoardData state)
    {
        if (state.Items.Select(i => i.Kind).SequenceEqual(Items.Select(i => i.Kind)))
        {
            for (var i = 0; i < Items.Count; i++) Items[i].Apply(state.Items[i]);
        }
        else
        {
            Items.Clear();
            foreach (var item in state.Items) Items.Add(BoardItemViewModel.FromData(item));
        }
        SetConnectors(state.Connectors);
    }

    private static void Push(List<string> stack, string snapshot)
    {
        stack.Add(snapshot);
        if (stack.Count > MaxHistory) stack.RemoveAt(0);
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ---------- Selection ----------

    public void SelectOnly(BoardItemViewModel item) => Select(new[] { item });

    public void SelectOnly(ConnectorViewModel connector)
    {
        foreach (var i in Items) i.IsSelected = false;
        foreach (var c in Connectors) c.IsSelected = c == connector;
    }

    public void Select(IEnumerable<BoardItemViewModel> items)
    {
        var selected = items.ToHashSet();
        foreach (var i in Items) i.IsSelected = selected.Contains(i);
        foreach (var c in Connectors) c.IsSelected = false;
    }

    public void SelectAll()
    {
        foreach (var i in Items) i.IsSelected = true;
        foreach (var c in Connectors) c.IsSelected = true;
    }

    public void ClearSelection() => Select(Array.Empty<BoardItemViewModel>());

    public void SelectWithin(Rect area, bool addToSelection)
    {
        foreach (var item in Items)
        {
            // Frames only count when fully enclosed, so a drag inside a frame picks its notes instead.
            var hit = item is FrameViewModel ? area.Contains(item.Bounds) : area.IntersectsWith(item.Bounds);
            item.IsSelected = hit || (addToSelection && item.IsSelected);
        }
        if (!addToSelection) foreach (var c in Connectors) c.IsSelected = false;
    }

    /// <summary>The selected items plus everything sitting inside selected frames.</summary>
    public List<BoardItemViewModel> SelectionWithFrameContents()
    {
        var frames = SelectedItems.OfType<FrameViewModel>().ToList();
        return Items.Where(i => i.IsSelected || frames.Any(f => f.Contains(i))).ToList();
    }

    /// <summary>The top-most note, text or picture under <paramref name="point"/>.</summary>
    public BoardItemViewModel? ItemAt(Point point, BoardItemViewModel? except = null) =>
        Items.LastOrDefault(i => i is not FrameViewModel && i != except && i.Bounds.Contains(point));

    /// <summary>Items whose text contains <paramref name="query"/>, in board order.</summary>
    public List<BoardItemViewModel> Find(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return new();
        return Items.Where(i => i is not ImageViewModel && i.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    /// <summary>Frames row by row, top to bottom and left to right.</summary>
    public IEnumerable<FrameViewModel> FramesInReadingOrder() =>
        Items.OfType<FrameViewModel>().OrderBy(f => Math.Round(f.Y / 200)).ThenBy(f => f.X);

    // ---------- Editing ----------

    public NoteViewModel AddNote(Point center, string color, double size = 200) =>
        Add(new NoteViewModel { Color = color }, center, size, size);

    public TextViewModel AddText(Point center) => Add(new TextViewModel(), center, 320, 64);

    public ImageViewModel AddImage(string imageId, Point center, double width, double height) =>
        Add(new ImageViewModel { ImageId = imageId }, center, width, height);

    public FrameViewModel AddFrame(Point center) =>
        Add(new FrameViewModel { Color = Palette.Frame, Text = $"Frame {Items.OfType<FrameViewModel>().Count() + 1}" }, center, 800, 500);

    private T Add<T>(T item, Point center, double width, double height) where T : BoardItemViewModel
    {
        item.Width = width;
        item.Height = height;
        item.X = center.X - width / 2;
        item.Y = center.Y - height / 2;
        Items.Add(item);
        SelectOnly(item);
        return item;
    }

    /// <summary>Adds an arrow, unless the two are the same item or already connected that way.</summary>
    public ConnectorViewModel? Connect(BoardItemViewModel from, BoardItemViewModel to)
    {
        if (from == to || Connectors.Any(c => c.From == from && c.To == to)) return null;
        var connector = new ConnectorViewModel(from, to, _lineScale);
        Connectors.Add(connector);
        return connector;
    }

    public void ReverseSelectedConnectors()
    {
        foreach (var connector in Connectors.Where(c => c.IsSelected)) connector.Reverse();
    }

    public void BeginMove() => _moving = SelectionWithFrameContents();

    public void MoveBy(double dx, double dy)
    {
        foreach (var item in _moving)
        {
            item.X += dx;
            item.Y += dy;
        }
    }

    public void DeleteSelected()
    {
        foreach (var connector in Connectors.Where(c => c.IsSelected).ToList()) RemoveConnector(connector);
        Remove(SelectionWithFrameContents());
    }

    /// <summary>Removes items along with any arrows attached to them.</summary>
    public void Remove(IReadOnlyCollection<BoardItemViewModel> items)
    {
        var doomed = items.ToHashSet();
        foreach (var connector in Connectors.Where(c => doomed.Contains(c.From) || doomed.Contains(c.To)).ToList())
            RemoveConnector(connector);
        foreach (var item in doomed) Items.Remove(item);
    }

    private void RemoveConnector(ConnectorViewModel connector)
    {
        connector.Detach();
        Connectors.Remove(connector);
    }

    /// <summary>The selection (with frame contents, arrows between them and pictures) as a standalone board, for copying.</summary>
    public BoardData CopySelection()
    {
        var data = ContentData(SelectionWithFrameContents());
        data.Images = ImagesFor(data.Items);
        return data;
    }

    /// <summary>Adds copies of a copied selection centered on <paramref name="center"/>, and selects them.</summary>
    public void Paste(BoardData clip, Point center)
    {
        if (clip.Items.Count == 0) return;
        var extent = Rect.Empty;
        foreach (var item in clip.Items) extent.Union(new Rect(item.X, item.Y, item.Width, item.Height));
        Insert(clip, center - new Point(extent.X + extent.Width / 2, extent.Y + extent.Height / 2));
    }

    public void DuplicateSelection() => Insert(CopySelection(), new Vector(24, 24));

    private void Insert(BoardData clip, Vector offset)
    {
        var imageIds = AddImages(clip.Images);
        var copies = new Dictionary<string, BoardItemViewModel>();
        ClearSelection();
        foreach (var data in clip.Items)
        {
            var item = BoardItemViewModel.FromData(data);
            item.Id = BoardItemViewModel.NewId();
            if (data.Id is not null) copies[data.Id] = item;
            RemapImage(item, imageIds);
            item.X += offset.X;
            item.Y += offset.Y;
            item.IsSelected = true;
            Items.Add(item);
        }
        foreach (var c in clip.Connectors)
            if (copies.TryGetValue(c.From, out var from) && copies.TryGetValue(c.To, out var to))
                Connect(from, to);
    }

    public void Recolor(string color)
    {
        var selected = SelectedItems.ToList();
        // Notes take the color; frames only when no notes are selected, so they don't turn into giant stickies.
        var notes = selected.OfType<NoteViewModel>().ToList();
        IEnumerable<BoardItemViewModel> targets = notes.Count > 0 ? notes : selected.OfType<FrameViewModel>();
        foreach (var item in targets) item.Color = color;
    }

    public void BringToFront()
    {
        foreach (var item in SelectedItems.ToList()) Items.Move(Items.IndexOf(item), Items.Count - 1);
    }

    public void SendToBack()
    {
        var selected = SelectedItems.ToList();
        for (var i = selected.Count - 1; i >= 0; i--) Items.Move(Items.IndexOf(selected[i]), 0);
    }
}
