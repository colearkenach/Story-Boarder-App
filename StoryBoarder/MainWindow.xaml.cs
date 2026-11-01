using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using StoryBoarder.Models;
using StoryBoarder.Services;
using StoryBoarder.ViewModels;

namespace StoryBoarder;

public partial class MainWindow : Window
{
    private const string BoardFilter = "Story Boarder board (*.storyboard)|*.storyboard|All files (*.*)|*.*";
    // Also matches .mlboard, the extension boards were saved with before the app was renamed.
    private const string OpenFilter = "Story Boarder board (*.storyboard)|*.storyboard;*.mlboard|All files (*.*)|*.*";
    private const string CsvFilter = "Miro CSV export (*.csv)|*.csv|All files (*.*)|*.*";
    private const double MinZoom = 0.05, MaxZoom = 4;

    public static readonly DependencyProperty TitleScaleProperty = DependencyProperty.Register(
        nameof(TitleScale), typeof(double), typeof(MainWindow), new PropertyMetadata(1.0));

    /// <summary>How much frame titles are enlarged so they stay readable when zoomed out.</summary>
    public double TitleScale
    {
        get => (double)GetValue(TitleScaleProperty);
        private set => SetValue(TitleScaleProperty, value);
    }

    private readonly BoardViewModel _board = new();
    private readonly ScaleTransform _zoom = new();
    private readonly TranslateTransform _pan = new();
    private readonly ScaleTransform _titleScale = new(); // applies TitleScale to every frame title

    private string? _filePath;
    private string _docName = "Untitled";
    private string _noteColor = Palette.DefaultNote;

    private string? _dragBefore; // board snapshot taken when a move or resize starts
    private string? _editBefore; // board snapshot taken when text editing starts
    private Point? _panStart;
    private Point _panOrigin;
    private Point? _selectStart;
    private Point _contextPoint; // board position of the last right-click

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _board;

        var view = new TransformGroup { Children = new TransformCollection { _zoom, _pan } };
        BoardItems.RenderTransform = view;
        GridBrush.Transform = view;

        _board.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BoardViewModel.IsDirty)) UpdateTitle();
        };
        Loaded += OnLoaded;
        UpdateTitle();
        UpdateNoteColorChip();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Viewport.Focus();
        OnViewChanged();
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1])) OpenPath(args[1]);
    }

    // ---------- Files ----------

    private void New_Click(object sender, RoutedEventArgs e) => NewBoard();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenWithDialog(OpenFilter);
    private void ImportCsv_Click(object sender, RoutedEventArgs e) => OpenWithDialog(CsvFilter);
    private void Save_Click(object sender, RoutedEventArgs e) => Save(saveAs: false);

    private void NewBoard()
    {
        if (!ConfirmDiscardChanges()) return;
        _board.Load(new BoardData());
        SetDocument(null, "Untitled");
        ResetView();
    }

    private void OpenWithDialog(string filter)
    {
        if (!ConfirmDiscardChanges()) return;
        var dialog = new OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog(this) == true) OpenPath(dialog.FileName);
    }

    private void OpenPath(string path)
    {
        try
        {
            var isCsv = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
            _board.Load(isCsv ? MiroCsvImporter.Import(path) : BoardFile.Load(path));
            // An imported CSV has no board file yet, so the first save asks where to put it.
            SetDocument(isCsv ? null : path, Path.GetFileNameWithoutExtension(path));
            ZoomToFit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't open {Path.GetFileName(path)}.\n\n{ex.Message}", "Story Boarder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool Save(bool saveAs)
    {
        EndEditing();
        var path = _filePath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog { Filter = BoardFilter, FileName = _docName, DefaultExt = ".storyboard" };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        try
        {
            BoardFile.Save(path, _board.ToData());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save {Path.GetFileName(path)}.\n\n{ex.Message}", "Story Boarder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        _board.IsDirty = false;
        SetDocument(path, Path.GetFileNameWithoutExtension(path));
        return true;
    }

    private bool ConfirmDiscardChanges()
    {
        EndEditing();
        if (!_board.IsDirty) return true;
        var answer = MessageBox.Show(this, $"Save changes to \"{_docName}\"?", "Story Boarder",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => Save(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void SetDocument(string? path, string name)
    {
        _filePath = path;
        _docName = name;
        UpdateTitle();
    }

    private void UpdateTitle() => Title = $"{_docName}{(_board.IsDirty ? " •" : "")} – Story Boarder";

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges()) e.Cancel = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && ConfirmDiscardChanges())
            OpenPath(files[0]);
    }

    // ---------- Toolbar ----------

    // Clicking any chrome finishes text editing first, so the edit lands in undo history before the action.
    private void Chrome_PreviewMouseDown(object sender, MouseButtonEventArgs e) => EndEditing();

    private void Undo_Click(object sender, RoutedEventArgs e) => _board.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _board.Redo();
    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNoteAt(ToBoard(ViewportCenter));
    private void AddFrame_Click(object sender, RoutedEventArgs e) => AddFrameAt(ToBoard(ViewportCenter));
    private void Swatch_Click(object sender, RoutedEventArgs e) => ApplyColor(ColorOf(sender));

    private void ApplyColor(string hex)
    {
        EndEditing();
        _noteColor = hex;
        UpdateNoteColorChip();
        _board.Change(() => _board.Recolor(hex));
    }

    private void UpdateNoteColorChip() =>
        NoteColorChip.Background = (Brush)new BrushConverter().ConvertFromString(_noteColor)!;

    // ---------- Creating and editing items ----------

    private void AddNoteAt(Point position)
    {
        EndEditing();
        var before = _board.Snapshot();
        BeginEdit(_board.AddNote(position, _noteColor), before);
    }

    private void AddFrameAt(Point position)
    {
        EndEditing();
        _board.Change(() => _board.AddFrame(position));
    }

    private void BeginEdit(BoardItemViewModel item, string? before = null)
    {
        EndEditing();
        _board.SelectOnly(item);
        _editBefore = before ?? _board.Snapshot();
        item.IsEditing = true;
    }

    private void EditSelection()
    {
        if (_board.SelectedItems.ToList() is [var only]) BeginEdit(only);
    }

    // Moving keyboard focus off the edit box ends editing (see EditBox_LostKeyboardFocus).
    private void EndEditing() => Viewport.Focus();

    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        var box = (TextBox)sender;
        Dispatcher.InvokeAsync(() =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }, DispatcherPriority.Input);
    }

    private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var item = ItemOf(sender);
        if (!item.IsEditing) return;
        item.IsEditing = false;
        if (_editBefore is { } before)
        {
            _editBefore = null;
            _board.Commit(before);
        }
    }

    private void EditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var finish = e.Key == Key.Escape
            || (e.Key == Key.Enter && (!box.AcceptsReturn || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)));
        if (!finish) return;
        EndEditing();
        e.Handled = true;
    }

    // ---------- Dragging items ----------

    private void Item_DragStarted(object sender, DragStartedEventArgs e)
    {
        EndEditing();
        var item = ItemOf(sender);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) item.IsSelected = !item.IsSelected;
        else if (!item.IsSelected) _board.SelectOnly(item);
        _dragBefore = _board.Snapshot();
        _board.BeginMove();
    }

    private void Item_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ItemOf(sender).IsSelected) _board.MoveBy(e.HorizontalChange, e.VerticalChange);
    }

    // Frame titles are counter-scaled, so their drag distances need scaling back to board units.
    private void FrameTitle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ItemOf(sender).IsSelected)
            _board.MoveBy(e.HorizontalChange * _titleScale.ScaleX, e.VerticalChange * _titleScale.ScaleY);
    }

    private void Resize_DragStarted(object sender, DragStartedEventArgs e)
    {
        EndEditing();
        _dragBefore = _board.Snapshot();
    }

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var item = ItemOf(sender);
        item.Width = Math.Max(item.MinSize, item.Width + e.HorizontalChange);
        item.Height = Math.Max(item.MinSize, item.Height + e.VerticalChange);
    }

    private void Drag_Completed(object sender, DragCompletedEventArgs e)
    {
        if (_dragBefore is not { } before) return;
        _dragBefore = null;
        _board.Commit(before);
    }

    private void Item_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        BeginEdit(ItemOf(sender));
        e.Handled = true;
    }

    private void FrameTitle_Loaded(object sender, RoutedEventArgs e) =>
        ((UIElement)sender).RenderTransform = _titleScale;

    // ---------- Context menus ----------

    private void Viewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        _contextPoint = ToBoard(e.GetPosition(Viewport));

    private void Item_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemOf(sender);
        if (!item.IsSelected) _board.SelectOnly(item);
    }

    private void MenuAddNote_Click(object sender, RoutedEventArgs e) => AddNoteAt(_contextPoint);
    private void MenuAddFrame_Click(object sender, RoutedEventArgs e) => AddFrameAt(_contextPoint);
    private void MenuEdit_Click(object sender, RoutedEventArgs e) => EditSelection();
    private void MenuColor_Click(object sender, RoutedEventArgs e) => ApplyColor(ColorOf(sender));
    private void MenuDuplicate_Click(object sender, RoutedEventArgs e) => _board.Change(_board.DuplicateSelection);
    private void MenuBringToFront_Click(object sender, RoutedEventArgs e) => _board.Change(_board.BringToFront);
    private void MenuSendToBack_Click(object sender, RoutedEventArgs e) => _board.Change(_board.SendToBack);
    private void MenuDelete_Click(object sender, RoutedEventArgs e) => _board.Change(_board.DeleteSelected);
    private void MenuSelectAll_Click(object sender, RoutedEventArgs e) => _board.SelectAll();

    // ---------- Panning, zooming and box selection ----------

    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        EndEditing();
        var position = e.GetPosition(Viewport);
        var modifiers = Keyboard.Modifiers;
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            AddNoteAt(ToBoard(position));
        }
        else if (e.ChangedButton == MouseButton.Left && modifiers.HasFlag(ModifierKeys.Shift))
        {
            _selectStart = position;
            UpdateSelectionBox(position);
            Viewport.CaptureMouse();
        }
        else if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
            if (e.ChangedButton == MouseButton.Left && !modifiers.HasFlag(ModifierKeys.Control)) _board.ClearSelection();
            _panStart = position;
            _panOrigin = new Point(_pan.X, _pan.Y);
            Viewport.Cursor = Cursors.ScrollAll;
            Viewport.CaptureMouse();
        }
        else
        {
            return;
        }
        e.Handled = true;
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(Viewport);
        if (_panStart is { } start)
        {
            _pan.X = _panOrigin.X + position.X - start.X;
            _pan.Y = _panOrigin.Y + position.Y - start.Y;
        }
        else if (_selectStart is not null)
        {
            UpdateSelectionBox(position);
        }
    }

    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectStart is { } start)
        {
            var area = new Rect(ToBoard(start), ToBoard(e.GetPosition(Viewport)));
            _board.SelectWithin(area, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        }
        Viewport.ReleaseMouseCapture();
    }

    private void Viewport_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _panStart = null;
        _selectStart = null;
        SelectionBox.Visibility = Visibility.Collapsed;
        Viewport.Cursor = null;
    }

    private void UpdateSelectionBox(Point position)
    {
        var area = new Rect(_selectStart!.Value, position);
        Canvas.SetLeft(SelectionBox, area.X);
        Canvas.SetTop(SelectionBox, area.Y);
        SelectionBox.Width = area.Width;
        SelectionBox.Height = area.Height;
        SelectionBox.Visibility = Visibility.Visible;
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(Viewport), Math.Pow(1.0015, e.Delta));
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAt(ViewportCenter, 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAt(ViewportCenter, 0.8);
    private void ZoomFit_Click(object sender, RoutedEventArgs e) => ZoomToFit();

    private Point ViewportCenter => new(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2);

    private Point ToBoard(Point screen) =>
        new((screen.X - _pan.X) / _zoom.ScaleX, (screen.Y - _pan.Y) / _zoom.ScaleY);

    private Point PointerOrCenter()
    {
        var pointer = Mouse.GetPosition(Viewport);
        var inside = new Rect(Viewport.RenderSize).Contains(pointer);
        return ToBoard(inside ? pointer : ViewportCenter);
    }

    // Zooms while keeping the board point under `screen` fixed.
    private void ZoomAt(Point screen, double factor)
    {
        var anchor = ToBoard(screen);
        var scale = Math.Clamp(_zoom.ScaleX * factor, MinZoom, MaxZoom);
        SetView(scale, screen.X - anchor.X * scale, screen.Y - anchor.Y * scale);
    }

    private void ZoomToFit()
    {
        if (_board.Extent() is not { } extent || Viewport.ActualWidth < 1)
        {
            ResetView();
            return;
        }
        // Leave room around the content, plus extra on top for frame titles.
        var area = new Rect(extent.X - 60, extent.Y - 140, extent.Width + 120, extent.Height + 200);
        var scale = Math.Clamp(Math.Min(Viewport.ActualWidth / area.Width, Viewport.ActualHeight / area.Height), MinZoom, 1);
        SetView(scale,
            (Viewport.ActualWidth - area.Width * scale) / 2 - area.X * scale,
            (Viewport.ActualHeight - area.Height * scale) / 2 - area.Y * scale);
    }

    private void ResetView() => SetView(1, 0, 0);

    private void SetView(double scale, double panX, double panY)
    {
        _zoom.ScaleX = _zoom.ScaleY = scale;
        _pan.X = panX;
        _pan.Y = panY;
        OnViewChanged();
    }

    private void OnViewChanged()
    {
        var scale = _zoom.ScaleX;
        ZoomText.Text = $"{scale * 100:0}%";
        TitleScale = _titleScale.ScaleX = _titleScale.ScaleY = Math.Clamp(1 / scale, 1, 10);
        // The dot grid turns to noise when zoomed far out.
        GridLayer.Visibility = scale < 0.3 ? Visibility.Hidden : Visibility.Visible;
    }

    // ---------- Keyboard ----------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (Keyboard.FocusedElement is TextBox or MenuItem)
        {
            // While typing in a note, only Save reaches the board.
            if (ctrl && e.Key == Key.S)
            {
                Save(saveAs: shift);
                e.Handled = true;
            }
            return;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.N when ctrl: NewBoard(); break;
            case Key.O when ctrl: OpenWithDialog(OpenFilter); break;
            case Key.I when ctrl: OpenWithDialog(CsvFilter); break;
            case Key.S when ctrl: Save(saveAs: shift); break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl: _board.Redo(); break;
            case Key.Z when ctrl: _board.Undo(); break;
            case Key.A when ctrl: _board.SelectAll(); break;
            case Key.D when ctrl: _board.Change(_board.DuplicateSelection); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomToFit(); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomAt(ViewportCenter, 1.25); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomAt(ViewportCenter, 0.8); break;
            case Key.Delete or Key.Back: _board.Change(_board.DeleteSelected); break;
            case Key.Enter: EditSelection(); break;
            case Key.Escape: _board.ClearSelection(); break;
            case Key.N when !ctrl: AddNoteAt(PointerOrCenter()); break;
            case Key.F when !ctrl: AddFrameAt(PointerOrCenter()); break;
            default: e.Handled = false; break;
        }
    }

    // ---------- Helpers ----------

    private static BoardItemViewModel ItemOf(object sender) =>
        (BoardItemViewModel)((FrameworkElement)sender).DataContext;

    private static string ColorOf(object sender) =>
        ((PaletteColor)((FrameworkElement)sender).DataContext).Hex;
}
