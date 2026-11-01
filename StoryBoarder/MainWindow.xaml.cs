using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private const string PictureFilter =
        "Pictures (*.png;*.jpg;*.gif;*.bmp;*.tif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp|All files (*.*)|*.*";
    private const string PngFilter = "PNG picture (*.png)|*.png";
    private const string ClipboardFormat = "StoryBoarder.Items";
    private const double MinZoom = 0.05, MaxZoom = 4;
    private const double MaxPictureSize = 480;   // longest side of a newly added picture, in board units
    private const double MaxExportPixels = 6000; // longest side of an exported PNG

    private static readonly string[] PictureExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp" };

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
    private readonly ScaleTransform _titleScale = new();  // applies TitleScale to every frame title
    private readonly ScaleTransform _handleScale = new(); // keeps resize and arrow handles a fixed size on screen

    private string? _filePath;
    private string _docName = "Untitled";
    private string _noteColor = Palette.DefaultNote;

    private string? _dragBefore;  // board snapshot taken when a move or resize starts
    private string? _editBefore;  // board snapshot taken when text editing starts
    private string? _nudgeBefore; // board snapshot taken when arrow-key nudging starts
    private Point? _panStart;
    private Point _panOrigin;
    private Point? _selectStart;
    private Point? _moveLast;     // board position of the pointer while dragging a frame by its body
    private Point _contextPoint;  // board position of the last right-click
    private bool _spaceHeld;
    private BoardItemViewModel? _connectFrom, _connectTarget;
    private List<BoardItemViewModel> _matches = new();
    private int _match = -1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _board;

        BoardItems.ItemsSource = new CompositeCollection
        {
            new CollectionContainer { Collection = _board.Items },
            new CollectionContainer { Collection = _board.Connectors },
        };
        var view = new TransformGroup { Children = new TransformCollection { _zoom, _pan } };
        BoardItems.RenderTransform = view;
        Overlay.RenderTransform = view;
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
    private void Export_Click(object sender, RoutedEventArgs e) => ExportPng();

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

    // Dropped pictures land on the board where they were dropped; anything else is opened.
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return;
        var pictures = files.Where(IsPictureFile).ToList();
        if (pictures.Count > 0) AddPictureFiles(pictures, ToBoard(e.GetPosition(Viewport)));
        else if (ConfirmDiscardChanges()) OpenPath(files[0]);
    }

    private void ExportPng(bool wholeBoard = false)
    {
        EndEditing();
        var items = wholeBoard ? new List<BoardItemViewModel>() : _board.SelectionWithFrameContents();
        if (items.Count == 0) items = _board.Items.ToList();
        if (items.Count == 0) return;
        var dialog = new SaveFileDialog { Filter = PngFilter, FileName = _docName, DefaultExt = ".png" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            RenderPng(items, dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Couldn't export {Path.GetFileName(dialog.FileName)}.\n\n{ex.Message}", "Story Boarder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Renders the area around `items` at up to 2x, keeping the picture under MaxExportPixels on its long side.
    private void RenderPng(IReadOnlyCollection<BoardItemViewModel> items, string path)
    {
        var bounds = Rect.Empty;
        foreach (var item in items) bounds.Union(item.Bounds);
        var scale = Math.Min(2, MaxExportPixels / Math.Max(bounds.Width, bounds.Height));
        // A margin all round, plus room for frame titles (which are enlarged when the picture is scaled down).
        var titleRoom = items.Any(i => i is FrameViewModel) ? 34 * Math.Clamp(1 / scale, 1, 10) : 0;
        var area = new Rect(bounds.X - 40, bounds.Y - 40 - titleRoom, bounds.Width + 80, bounds.Height + 80 + titleRoom);
        scale = Math.Min(2, MaxExportPixels / Math.Max(area.Width, area.Height));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(area.Width * scale), (int)Math.Ceiling(area.Height * scale),
            96, 96, PixelFormats.Pbgra32);

        // Point the view at the area, without selection outlines, render, then put everything back.
        var (zoom, panX, panY) = (_zoom.ScaleX, _pan.X, _pan.Y);
        var selectedItems = _board.SelectedItems.ToList();
        var selectedConnectors = _board.Connectors.Where(c => c.IsSelected).ToList();
        _board.ClearSelection();
        try
        {
            SetView(scale, -area.X * scale, -area.Y * scale);
            UpdateLayout();
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle((Brush)FindResource("BoardBrush"), null, new Rect(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Render(background);
            bitmap.Render(BoardItems);
        }
        finally
        {
            SetView(zoom, panX, panY);
            foreach (var item in selectedItems) item.IsSelected = true;
            foreach (var connector in selectedConnectors) connector.IsSelected = true;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ---------- Toolbar ----------

    // Clicking any chrome finishes text editing first, so the edit lands in undo history before the action.
    private void Chrome_PreviewMouseDown(object sender, MouseButtonEventArgs e) => EndEditing();

    private void Undo_Click(object sender, RoutedEventArgs e) => _board.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => _board.Redo();
    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNoteAt(ToBoard(ViewportCenter));
    private void AddText_Click(object sender, RoutedEventArgs e) => AddTextAt(ToBoard(ViewportCenter));
    private void AddFrame_Click(object sender, RoutedEventArgs e) => AddFrameAt(ToBoard(ViewportCenter));
    private void AddImage_Click(object sender, RoutedEventArgs e) => AddPicturesWithDialog(ToBoard(ViewportCenter));
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

    private void Frames_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var frame in _board.FramesInReadingOrder())
        {
            // A TextBlock header, so underscores in frame names aren't taken as access keys.
            var title = string.IsNullOrWhiteSpace(frame.Text) ? "(untitled frame)" : frame.Text;
            var item = new MenuItem { Header = new TextBlock { Text = title } };
            item.Click += (_, _) =>
            {
                _board.SelectOnly(frame);
                ZoomToItem(frame);
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No frames yet", IsEnabled = false });
        menu.IsOpen = true;
    }

    // ---------- Search ----------

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    // Typing highlights every match; Enter steps through them one at a time.
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _matches = _board.Find(SearchBox.Text);
        _match = -1;
        _board.Select(_matches);
        SearchCount.Text = SearchBox.Text.Trim().Length == 0 ? "" : $"{_matches.Count} found";
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
            Viewport.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ShowMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        }
    }

    private void ShowMatch(int step)
    {
        _matches = _board.Find(SearchBox.Text); // the board may have changed since the last keystroke
        if (_matches.Count == 0) return;
        _match = _match < 0 ? (step > 0 ? 0 : _matches.Count - 1) : (_match + step + _matches.Count) % _matches.Count;
        var item = _matches[_match];
        _board.SelectOnly(item);
        ZoomToItem(item);
        SearchCount.Text = $"{_match + 1} of {_matches.Count}";
    }

    // ---------- Creating and editing items ----------

    private void AddNoteAt(Point position)
    {
        EndEditing();
        var before = _board.Snapshot();
        BeginEdit(_board.AddNote(position, _noteColor), before);
    }

    // New text starts empty and in edit mode; if nothing gets typed, it disappears again.
    private void AddTextAt(Point position)
    {
        EndEditing();
        var before = _board.Snapshot();
        BeginEdit(_board.AddText(position), before);
    }

    private void AddFrameAt(Point position)
    {
        EndEditing();
        _board.Change(() => _board.AddFrame(position));
    }

    private void AddTextNote(string text, Point position)
    {
        var before = _board.Snapshot();
        _board.AddNote(position, _noteColor, MiroCsvImporter.NoteSize(text)).Text = text;
        _board.Commit(before);
    }

    private void AddPicturesWithDialog(Point position)
    {
        EndEditing();
        var dialog = new OpenFileDialog { Filter = PictureFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddPictureFiles(dialog.FileNames, position);
    }

    private void AddPictureFiles(IEnumerable<string> paths, Point position)
    {
        var pictures = new List<byte[]>();
        var failed = 0;
        foreach (var path in paths)
        {
            try
            {
                pictures.Add(File.ReadAllBytes(path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }
        AddPictures(pictures, position, failed);
    }

    private void AddPictures(IEnumerable<byte[]> pictures, Point position, int failed = 0)
    {
        EndEditing();
        var before = _board.Snapshot();
        var added = new List<BoardItemViewModel>();
        foreach (var bytes in pictures)
        {
            string id;
            try
            {
                id = ImageStore.Add(bytes);
            }
            catch (Exception e) when (e is NotSupportedException or FormatException or IOException or ArgumentException)
            {
                failed++;
                continue;
            }
            var image = ImageStore.Get(id)!;
            var scale = Math.Min(1, MaxPictureSize / Math.Max(image.PixelWidth, image.PixelHeight));
            var offset = added.Count * 40; // fan several pictures out so they don't stack exactly
            added.Add(_board.AddImage(id, position + new Vector(offset, offset), image.PixelWidth * scale, image.PixelHeight * scale));
        }
        _board.Select(added);
        _board.Commit(before);
        if (failed > 0)
            MessageBox.Show(this, failed == 1 ? "A file couldn't be read as a picture." : $"{failed} files couldn't be read as pictures.",
                "Story Boarder", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BeginEdit(BoardItemViewModel item, string? before = null)
    {
        EndEditing();
        _board.SelectOnly(item);
        if (item is ImageViewModel) return; // pictures have no text
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
        // Empty text would be invisible, so it goes away.
        if (item is TextViewModel && string.IsNullOrWhiteSpace(item.Text)) _board.Remove(new[] { item });
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

    // ---------- Copy and paste ----------

    private bool CopySelection()
    {
        var clip = _board.CopySelection();
        if (clip.Items.Count == 0) return false;
        var data = new DataObject();
        data.SetData(ClipboardFormat, BoardFile.Serialize(clip));
        // Plain text too, so copied notes can be pasted into other apps.
        var text = string.Join("\n\n", clip.Items.Select(i => i.Text).Where(t => t.Length > 0));
        if (text.Length > 0) data.SetText(text);
        try
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(this, $"Couldn't copy to the clipboard.\n\n{ex.Message}", "Story Boarder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void CutSelection()
    {
        if (CopySelection()) _board.Change(_board.DeleteSelected);
    }

    // Pastes copied board items, a picture, picture files or text (as a sticky note), in that order of preference.
    private void Paste(Point position)
    {
        EndEditing();
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return;
            if (data.GetData(ClipboardFormat) is string json)
                _board.Change(() => _board.Paste(BoardFile.Deserialize(json), position));
            else if (data.GetData("PNG") is MemoryStream png)
                AddPictures(new[] { png.ToArray() }, position);
            else if (data.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsPictureFile))
                AddPictureFiles(files.Where(IsPictureFile), position);
            else if (data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0)
                AddTextNote(text.Trim(), position);
            else if (data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
                AddPictures(new[] { EncodePng(bitmap) }, position);
        }
        catch (Exception ex) when (ex is ExternalException or JsonException)
        {
            MessageBox.Show(this, $"Couldn't paste that.\n\n{ex.Message}", "Story Boarder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // ---------- Dragging items ----------

    // Selects the item (Ctrl toggles it instead) and starts moving the selection, frames' contents included.
    private bool BeginItemDrag(BoardItemViewModel item)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) item.IsSelected = !item.IsSelected;
        else if (!item.IsSelected) _board.SelectOnly(item);
        if (!item.IsSelected) return false;
        _dragBefore = _board.Snapshot();
        _board.BeginMove();
        return true;
    }

    private void Item_DragStarted(object sender, DragStartedEventArgs e)
    {
        EndEditing();
        BeginItemDrag(ItemOf(sender));
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
        // The handle is counter-scaled too, so its drag distance is scaled back to board units.
        var width = Math.Max(item.MinSize, item.Width + e.HorizontalChange * _handleScale.ScaleX);
        var height = Math.Max(item.MinSize, item.Height + e.VerticalChange * _handleScale.ScaleY);
        if (item is ImageViewModel) height = width * item.Height / item.Width; // pictures keep their shape
        item.Width = width;
        item.Height = height;
    }

    private void Drag_Completed(object sender, DragCompletedEventArgs e) => FinishDrag();

    private void FinishDrag()
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

    private void Handle_Loaded(object sender, RoutedEventArgs e) =>
        ((UIElement)sender).RenderTransform = _handleScale;

    // ---------- Drawing arrows ----------

    private void Connect_DragStarted(object sender, DragStartedEventArgs e) => StartConnect(ItemOf(sender));
    private void Connect_DragDelta(object sender, DragDeltaEventArgs e) => UpdateConnect(ToBoard(Mouse.GetPosition(Viewport)));
    private void Connect_DragCompleted(object sender, DragCompletedEventArgs e) =>
        FinishConnect(ToBoard(Mouse.GetPosition(Viewport)), e.Canceled);

    private void StartConnect(BoardItemViewModel from)
    {
        EndEditing();
        _connectFrom = from;
        ConnectPreview.StrokeThickness = ConnectorViewModel.LineWidth * _board.LineScale;
        ConnectPreview.Data = null;
        ConnectPreview.Visibility = Visibility.Visible;
    }

    private void UpdateConnect(Point point)
    {
        if (_connectFrom is not { } from) return;
        SetConnectTarget(_board.ItemAt(point, from));
        ConnectPreview.Data = ConnectorViewModel.Build(from.Bounds, _connectTarget?.Bounds ?? new Rect(point, point), _board.LineScale);
    }

    private void FinishConnect(Point point, bool canceled)
    {
        var (from, target) = (_connectFrom, _connectTarget);
        _connectFrom = null;
        SetConnectTarget(null);
        ConnectPreview.Visibility = Visibility.Collapsed;
        if (from is null || canceled) return;

        if (target is not null)
        {
            _board.Change(() => _board.Connect(from, target));
        }
        else if (!from.Bounds.Contains(point))
        {
            // Dropped on empty space: a new note appears there, already connected, ready for typing.
            var before = _board.Snapshot();
            var note = from is NoteViewModel
                ? _board.AddNote(point, from.Color, from.Width)
                : _board.AddNote(point, _noteColor);
            _board.Connect(from, note);
            BeginEdit(note, before);
        }
    }

    private void SetConnectTarget(BoardItemViewModel? target)
    {
        if (_connectTarget is not null) _connectTarget.IsDropTarget = false;
        _connectTarget = target;
        if (target is not null) target.IsDropTarget = true;
    }

    private void Connector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        EndEditing();
        var connector = ConnectorOf(sender);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) connector.IsSelected = !connector.IsSelected;
        else _board.SelectOnly(connector);
        e.Handled = true;
    }

    private void Connector_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var connector = ConnectorOf(sender);
        if (!connector.IsSelected) _board.SelectOnly(connector);
    }

    // ---------- Context menus ----------

    private void Viewport_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        _contextPoint = ToBoard(e.GetPosition(Viewport));

    private void Item_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemOf(sender);
        if (!item.IsSelected) _board.SelectOnly(item);
    }

    private void MenuAddNote_Click(object sender, RoutedEventArgs e) => AddNoteAt(_contextPoint);
    private void MenuAddText_Click(object sender, RoutedEventArgs e) => AddTextAt(_contextPoint);
    private void MenuAddFrame_Click(object sender, RoutedEventArgs e) => AddFrameAt(_contextPoint);
    private void MenuAddImage_Click(object sender, RoutedEventArgs e) => AddPicturesWithDialog(_contextPoint);
    private void MenuPaste_Click(object sender, RoutedEventArgs e) => Paste(_contextPoint);
    private void MenuEdit_Click(object sender, RoutedEventArgs e) => EditSelection();
    private void MenuColor_Click(object sender, RoutedEventArgs e) => ApplyColor(ColorOf(sender));
    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopySelection();
    private void MenuCut_Click(object sender, RoutedEventArgs e) => CutSelection();
    private void MenuDuplicate_Click(object sender, RoutedEventArgs e) => _board.Change(_board.DuplicateSelection);
    private void MenuBringToFront_Click(object sender, RoutedEventArgs e) => _board.Change(_board.BringToFront);
    private void MenuSendToBack_Click(object sender, RoutedEventArgs e) => _board.Change(_board.SendToBack);
    private void MenuReverse_Click(object sender, RoutedEventArgs e) => _board.Change(_board.ReverseSelectedConnectors);
    private void MenuExport_Click(object sender, RoutedEventArgs e) => ExportPng();
    private void MenuExportBoard_Click(object sender, RoutedEventArgs e) => ExportPng(wholeBoard: true);
    private void MenuDelete_Click(object sender, RoutedEventArgs e) => _board.Change(_board.DeleteSelected);
    private void MenuSelectAll_Click(object sender, RoutedEventArgs e) => _board.SelectAll();

    // ---------- Panning, zooming, box selection and moving frames ----------

    // Middle-drag, or left-drag while holding Space, pans from anywhere, even on top of notes.
    private void Viewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var spaceDrag = e.ChangedButton == MouseButton.Left && _spaceHeld;
        if (e.ChangedButton != MouseButton.Middle && !spaceDrag) return;
        EndEditing();
        StartPan(e.GetPosition(Viewport));
        e.Handled = true;
    }

    // Left clicks that no item handled: on a frame's body, on empty board, or a double-click.
    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        EndEditing();
        var position = e.GetPosition(Viewport);
        var modifiers = Keyboard.Modifiers;
        if (e.ClickCount == 2)
        {
            AddNoteAt(ToBoard(position));
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            _selectStart = position;
            UpdateSelectionBox(position);
            Viewport.CaptureMouse();
        }
        else if ((e.OriginalSource as FrameworkElement)?.DataContext is FrameViewModel frame)
        {
            // Dragging a frame's body moves the frame and everything in it.
            if (BeginItemDrag(frame))
            {
                _moveLast = ToBoard(position);
                Viewport.CaptureMouse();
            }
        }
        else
        {
            if (!modifiers.HasFlag(ModifierKeys.Control)) _board.ClearSelection();
            StartPan(position);
        }
        e.Handled = true;
    }

    private void StartPan(Point position)
    {
        _panStart = position;
        _panOrigin = new Point(_pan.X, _pan.Y);
        Viewport.Cursor = Cursors.ScrollAll;
        Viewport.CaptureMouse();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e) => OnPointerMoved(e.GetPosition(Viewport));

    private void OnPointerMoved(Point position)
    {
        if (_panStart is { } start)
        {
            _pan.X = _panOrigin.X + position.X - start.X;
            _pan.Y = _panOrigin.Y + position.Y - start.Y;
        }
        else if (_moveLast is { } last)
        {
            var point = ToBoard(position);
            _board.MoveBy(point.X - last.X, point.Y - last.Y);
            _moveLast = point;
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
        var wasMoving = _moveLast is not null;
        _panStart = null;
        _selectStart = null;
        _moveLast = null;
        SelectionBox.Visibility = Visibility.Collapsed;
        Viewport.Cursor = null;
        if (wasMoving) FinishDrag();
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

    // Frames fill most of the view; smaller items come up to a readable zoom without filling the screen.
    private void ZoomToItem(BoardItemViewModel item)
    {
        var (width, height) = (Viewport.ActualWidth, Viewport.ActualHeight);
        var fit = Math.Min(width * 0.8 / item.Width, height * 0.8 / item.Height);
        var scale = item is FrameViewModel ? Math.Min(fit, 1.5) : Math.Min(fit, Math.Max(_zoom.ScaleX, 0.8));
        scale = Math.Clamp(scale, MinZoom, MaxZoom);
        var center = item.Center;
        SetView(scale, width / 2 - center.X * scale, height / 2 - center.Y * scale);
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
        _handleScale.ScaleX = _handleScale.ScaleY = 1 / scale;
        _board.LineScale = TitleScale;
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
            // While typing, only Save and Search reach the board.
            if (ctrl && e.Key == Key.S) Save(saveAs: shift);
            else if (ctrl && e.Key == Key.F) FocusSearch();
            else return;
            e.Handled = true;
            return;
        }

        var arrow = e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        if (!arrow) FinishNudge();

        e.Handled = true;
        switch (e.Key)
        {
            case Key.N when ctrl: NewBoard(); break;
            case Key.O when ctrl: OpenWithDialog(OpenFilter); break;
            case Key.I when ctrl: OpenWithDialog(CsvFilter); break;
            case Key.S when ctrl: Save(saveAs: shift); break;
            case Key.E when ctrl: ExportPng(); break;
            case Key.F when ctrl: FocusSearch(); break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl: _board.Redo(); break;
            case Key.Z when ctrl: _board.Undo(); break;
            case Key.A when ctrl: _board.SelectAll(); break;
            case Key.C when ctrl: CopySelection(); break;
            case Key.X when ctrl: CutSelection(); break;
            case Key.V when ctrl: Paste(PointerOrCenter()); break;
            case Key.D when ctrl: _board.Change(_board.DuplicateSelection); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomToFit(); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomAt(ViewportCenter, 1.25); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomAt(ViewportCenter, 0.8); break;
            case Key.Delete or Key.Back: _board.Change(_board.DeleteSelected); break;
            case Key.Enter: EditSelection(); break;
            case Key.Escape: _board.ClearSelection(); break;
            case Key.N when !ctrl: AddNoteAt(PointerOrCenter()); break;
            case Key.T when !ctrl: AddTextAt(PointerOrCenter()); break;
            case Key.F when !ctrl: AddFrameAt(PointerOrCenter()); break;
            case Key.Space:
                if (!e.IsRepeat)
                {
                    _spaceHeld = true;
                    Mouse.OverrideCursor = Cursors.Hand;
                }
                break;
            case var _ when arrow: Nudge(e.Key, far: shift); break;
            default: e.Handled = false; break;
        }
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld) StopSpacePan();
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) FinishNudge();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        StopSpacePan();
        FinishNudge();
    }

    private void StopSpacePan()
    {
        _spaceHeld = false;
        Mouse.OverrideCursor = null;
    }

    // Arrow keys nudge the selection (one undo step per burst of presses), or scroll the view if nothing is selected.
    private void Nudge(Key key, bool far)
    {
        var (dx, dy) = key switch
        {
            Key.Left => (-1, 0),
            Key.Right => (1, 0),
            Key.Up => (0, -1),
            _ => (0, 1),
        };
        if (!_board.SelectedItems.Any())
        {
            var step = far ? 400 : 100;
            SetView(_zoom.ScaleX, _pan.X - dx * step, _pan.Y - dy * step);
            return;
        }
        if (_nudgeBefore is null)
        {
            _nudgeBefore = _board.Snapshot();
            _board.BeginMove();
        }
        var distance = far ? 50 : 10;
        _board.MoveBy(dx * distance, dy * distance);
    }

    private void FinishNudge()
    {
        if (_nudgeBefore is not { } before) return;
        _nudgeBefore = null;
        _board.Commit(before);
    }

    // ---------- Helpers ----------

    private static bool IsPictureFile(string path) =>
        PictureExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static BoardItemViewModel ItemOf(object sender) =>
        (BoardItemViewModel)((FrameworkElement)sender).DataContext;

    private static ConnectorViewModel ConnectorOf(object sender) =>
        (ConnectorViewModel)((FrameworkElement)sender).DataContext;

    private static string ColorOf(object sender) =>
        ((PaletteColor)((FrameworkElement)sender).DataContext).Hex;
}
