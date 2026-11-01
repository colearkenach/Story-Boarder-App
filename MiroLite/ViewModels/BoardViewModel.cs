using System.Collections.ObjectModel;
using System.Windows;
using MiroLite.Models;
using MiroLite.Services;

namespace MiroLite.ViewModels;

/// <summary>
/// The board's items plus selection, moving and undo/redo. Undo works on whole-board
/// snapshots: take <see cref="Snapshot"/> before a change, then <see cref="Commit"/> it.
/// </summary>
public sealed class BoardViewModel : ObservableObject
{
    private const int MaxHistory = 200;

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private List<BoardItemViewModel> _moving = new();
    private bool _isDirty;

    public ObservableCollection<BoardItemViewModel> Items { get; } = new();

    public IEnumerable<BoardItemViewModel> SelectedItems => Items.Where(i => i.IsSelected);

    public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Load(BoardData data)
    {
        Items.Clear();
        foreach (var item in data.Items) Items.Add(BoardItemViewModel.FromData(item));
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
        OnHistoryChanged();
    }

    public BoardData ToData() => new() { Items = Items.Select(i => i.ToData()).ToList() };

    public Rect? Extent()
    {
        if (Items.Count == 0) return null;
        var extent = Rect.Empty;
        foreach (var item in Items) extent.Union(item.Bounds);
        return extent;
    }

    // ---------- Undo / redo ----------

    public string Snapshot() => BoardFile.Serialize(ToData());

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
            return;
        }
        Items.Clear();
        foreach (var item in state.Items) Items.Add(BoardItemViewModel.FromData(item));
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

    public void SelectOnly(BoardItemViewModel item)
    {
        foreach (var i in Items) i.IsSelected = i == item;
    }

    public void SelectAll()
    {
        foreach (var i in Items) i.IsSelected = true;
    }

    public void ClearSelection()
    {
        foreach (var i in Items) i.IsSelected = false;
    }

    public void SelectWithin(Rect area, bool addToSelection)
    {
        foreach (var item in Items)
        {
            // Frames only count when fully enclosed, so a drag inside a frame picks its notes instead.
            var hit = item is FrameViewModel ? area.Contains(item.Bounds) : area.IntersectsWith(item.Bounds);
            item.IsSelected = hit || (addToSelection && item.IsSelected);
        }
    }

    // The selection plus everything sitting inside selected frames.
    private List<BoardItemViewModel> SelectionWithFrameContents()
    {
        var frames = SelectedItems.OfType<FrameViewModel>().ToList();
        return Items.Where(i => i.IsSelected || frames.Any(f => f.Contains(i))).ToList();
    }

    // ---------- Editing ----------

    public NoteViewModel AddNote(Point center, string color) =>
        Add(new NoteViewModel { Color = color }, center, 200, 200);

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
        foreach (var item in SelectionWithFrameContents()) Items.Remove(item);
    }

    public void DuplicateSelection()
    {
        var source = SelectionWithFrameContents();
        ClearSelection();
        foreach (var item in source)
        {
            var copy = BoardItemViewModel.FromData(item.ToData());
            copy.X += 24;
            copy.Y += 24;
            copy.IsSelected = true;
            Items.Add(copy);
        }
    }

    public void Recolor(string color)
    {
        var selected = SelectedItems.ToList();
        // A mixed selection recolors just the notes, so frames don't turn into giant stickies.
        var targets = selected.Any(i => i is NoteViewModel) ? selected.Where(i => i is NoteViewModel) : selected;
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
