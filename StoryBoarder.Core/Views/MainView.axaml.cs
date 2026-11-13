using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StoryBoarder.Controls;
using StoryBoarder.Models;
using StoryBoarder.Services;
using StoryBoarder.ViewModels;

namespace StoryBoarder.Views;

/// <summary>
/// The whole app UI: toolbar, board, pan/zoom, dragging, editing, menus and files. It runs inside a window
/// on desktop and fills the screen on phones and tablets.
/// </summary>
public partial class MainView : UserControl
{
    private const double MinZoom = 0.05, MaxZoom = 4;
    private const double MaxPictureSize = 480;   // longest side of a newly added picture, in board units
    private const double MaxExportPixels = 6000; // longest side of an exported PNG
    private const double CompactWidth = 1000;    // narrower than this, the toolbar drops labels
    private const double MouseSlop = 3, TouchSlop = 10; // how far a press moves before it becomes a drag

    private static readonly string[] PictureExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private static readonly FilePickerFileType BoardFiles = new("Story Boarder board")
    {
        // .mlboard is what boards were saved as before the app was renamed.
        Patterns = ["*.storyboard", "*.mlboard"],
        // Android has no type for .storyboard files, so let any file be picked there.
        MimeTypes = ["*/*"],
    };

    private static readonly FilePickerFileType BoardSaveType = new("Story Boarder board")
    {
        Patterns = ["*.storyboard"],
        MimeTypes = ["application/octet-stream"],
    };

    private static readonly FilePickerFileType CsvFiles = new("CSV export")
    {
        Patterns = ["*.csv"],
        MimeTypes = ["text/csv", "text/comma-separated-values", "application/csv", "text/plain", "*/*"],
    };

    private static readonly FilePickerFileType PictureFiles = new("Pictures")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp", "*.webp"],
        MimeTypes = ["image/*"],
    };

    private static readonly FilePickerFileType PngFile = new("PNG picture")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
    };

    // Copied board items, for pasting. Platforms that keep only text on the clipboard (Android) fall back to
    // the in-app copy, recognized by the text that was copied alongside it.
    private static readonly DataFormat<string> ItemsFormat = DataFormat.CreateStringApplicationFormat("StoryBoarder.Items");
    private static (string Json, string Text)? _lastCopy;

    public static readonly StyledProperty<double> TitleScaleProperty =
        AvaloniaProperty.Register<MainView, double>(nameof(TitleScale), 1);

    public static readonly StyledProperty<ITransform?> TitleTransformProperty =
        AvaloniaProperty.Register<MainView, ITransform?>(nameof(TitleTransform));

    public static readonly StyledProperty<string> DocumentTitleProperty =
        AvaloniaProperty.Register<MainView, string>(nameof(DocumentTitle), "Untitled – Story Boarder");

    /// <summary>The view that's showing, for app-wide error reporting.</summary>
    public static MainView? Current { get; private set; }

    /// <summary>Set by the Android app to put an ad banner under the board. Not used on desktop.</summary>
    public static Func<Control>? AdBannerFactory { get; set; }

    /// <summary>Set by the Android app when the user can change their ad privacy choices (shown in the File menu).</summary>
    public static Action? PrivacyOptions { get; set; }

    /// <summary>Set by the Android app: the one-time purchase that removes ads ("Remove ads" in the File menu).</summary>
    public static IPremiumStore? Store { get; set; }

    /// <summary>Set by the Android app: Google Play requires a link to the privacy policy inside the app.</summary>
    public static Uri? PrivacyPolicy { get; set; }

    private bool HasPremium => Store?.IsPremium == true;

    private readonly BoardViewModel _board = new();
    private readonly bool _isMobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Cursor _resizeCursor = new(StandardCursorType.BottomRightCorner);
    private readonly Cursor _connectCursor = new(StandardCursorType.Cross);
    private readonly Cursor _handCursor = new(StandardCursorType.Hand);
    private readonly Cursor _panCursor = new(StandardCursorType.SizeAll);

    private double _zoom = 1, _panX, _panY;
    private IStorageFile? _document;
    private string? _documentBookmark;
    private string _docName = "Untitled";
    private string _noteColor = Palette.DefaultNote;
    private bool _touchMode;
    private bool _started;

    private string? _dragBefore;  // board snapshot taken when a move or resize starts
    private string? _editBefore;  // board snapshot taken when text editing starts
    private string? _nudgeBefore; // board snapshot taken when arrow-key nudging starts
    private Point _contextPoint;  // board position of the last context menu
    private bool _spaceHeld;
    private BoardItemViewModel? _connectFrom, _connectTarget;
    private List<BoardItemViewModel> _matches = new();
    private int _match = -1;
    private TaskCompletionSource<string?>? _dialog;

    // ---------- Pointer gesture state ----------

    private enum Gesture { None, Pending, Pan, Move, Resize, Connect, BoxSelect, Pinch }

    private Gesture _gesture;
    private Gesture _pendingAs;           // what a Pending press becomes once it moves: Move or Pan
    private IPointer? _pointer;           // the pointer driving the gesture
    private Point _pressPoint;            // where it went down, in viewport coordinates
    private bool _pressIsTouch;
    private object? _pressTarget;         // item, arrow or null (empty board) under the press
    private bool _clearOnTap;             // a tap on empty board clears the selection
    private Point _panStart, _panOrigin;  // pointer position and pan offset when panning (re)started
    private Point _lastBoard;             // board position of the pointer while moving items
    private BoardItemViewModel? _resizeItem;
    private Size _resizeStart;
    private Point? _rightPress;           // a right-click opens its menu on release
    private readonly Dictionary<IPointer, Point> _touches = new();
    private double _pinchDistance, _pinchZoom;
    private Point _pinchAnchor;           // board point under the fingers' midpoint when the pinch started

    public MainView()
    {
        InitializeComponent();
        DataContext = _board;
        Adorner.Board = _board;

        _board.LayoutChanged += OnBoardLayoutChanged;
        _board.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BoardViewModel.IsDirty)) UpdateTitle();
            // CanUndo changes with every committed change, undo and redo.
            if (e.PropertyName == nameof(BoardViewModel.CanUndo) && _isMobile) RestartAutosave();
        };

        Viewport.PointerPressed += Viewport_PointerPressed;
        Viewport.PointerMoved += Viewport_PointerMoved;
        Viewport.PointerReleased += Viewport_PointerReleased;
        Viewport.PointerCaptureLost += Viewport_PointerCaptureLost;
        Viewport.PointerWheelChanged += Viewport_PointerWheelChanged;
        Viewport.PointerExited += (_, _) =>
        {
            if (_gesture == Gesture.None) SetHover(null);
        };
        _holdTimer.Tick += (_, _) => OnHold();
        _autosaveTimer.Tick += (_, _) =>
        {
            _autosaveTimer.Stop();
            SaveSession();
        };

        foreach (var swatches in new[] { Swatches, ToolbarSwatches, CompactSwatches, SelectionSwatches })
            swatches.AddHandler(Button.ClickEvent, Swatch_Click);
        SearchBox.TextChanged += Search_TextChanged;
        CompactSearchBox.TextChanged += Search_TextChanged;
        AddHandler(LostFocusEvent, OnLostFocus);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SizeChanged += (_, e) => Classes.Set("compact", e.NewSize.Width < CompactWidth);
        Viewport.SizeChanged += (_, _) =>
        {
            if (_fitOnResize) ZoomToFit();
        };

        Classes.Set("mobile", _isMobile);
        SetTouchMode(_isMobile);
        UpdateTitle();
        UpdateNoteColorChip();
        OnViewChanged();
    }

    public double TitleScale { get => GetValue(TitleScaleProperty); private set => SetValue(TitleScaleProperty, value); }
    public ITransform? TitleTransform { get => GetValue(TitleTransformProperty); private set => SetValue(TitleTransformProperty, value); }

    /// <summary>"Name • – Story Boarder", for the window title (the dot means unsaved changes).</summary>
    public string DocumentTitle { get => GetValue(DocumentTitleProperty); private set => SetValue(DocumentTitleProperty, value); }

    private TopLevel? Top => TopLevel.GetTopLevel(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Current = this;
        if (Top is not { } top) return;
        top.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        top.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        top.BackRequested += OnBackRequested;
        if (top.InputPane is { } inputPane) inputPane.StateChanged += OnInputPaneChanged;
        if (Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
            lifetime.Deactivated += OnAppDeactivated;

        if (Store is { } store) store.Changed += UpdateAdSlot;
        UpdateAdSlot();

        if (_started) return;
        _started = true;
        Dispatcher.UIThread.Post(() => Viewport.Focus(), DispatcherPriority.Loaded);
        if (_isMobile) _ = RestoreSessionAsync();
    }

    // The ad banner, unless the user has Premium. Removing it destroys the native ad view.
    private void UpdateAdSlot()
    {
        var showAds = AdBannerFactory is not null && !HasPremium;
        if (showAds && AdSlot.Content is null) AdSlot.Content = AdBannerFactory!();
        else if (!showAds) AdSlot.Content = null;
        AdSlot.IsVisible = AdSlot.Content is not null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_isMobile) SaveSession();
        if (Top is { } top)
        {
            top.RemoveHandler(KeyDownEvent, OnKeyDown);
            top.RemoveHandler(KeyUpEvent, OnKeyUp);
            top.BackRequested -= OnBackRequested;
            if (top.InputPane is { } inputPane) inputPane.StateChanged -= OnInputPaneChanged;
        }
        if (Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime lifetime)
            lifetime.Deactivated -= OnAppDeactivated;
        if (Store is { } store) store.Changed -= UpdateAdSlot;
        if (Current == this) Current = null;
    }

    // ---------- Files ----------

    private void New_Click(object? sender, RoutedEventArgs e) => _ = NewBoardAsync();
    private void Open_Click(object? sender, RoutedEventArgs e) => _ = OpenWithPickerAsync(csv: false);
    private void ImportCsv_Click(object? sender, RoutedEventArgs e) => _ = OpenWithPickerAsync(csv: true);
    private void Save_Click(object? sender, RoutedEventArgs e) => _ = SaveAsync(saveAs: false);
    private void Export_Click(object? sender, RoutedEventArgs e) => _ = ExportPngAsync();

    private void FileMenu_Click(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(MenuEntry("New board", () => _ = NewBoardAsync(), "Ctrl+N"));
        menu.Items.Add(MenuEntry("Open…", () => _ = OpenWithPickerAsync(csv: false), "Ctrl+O"));
        menu.Items.Add(MenuEntry("Save", () => _ = SaveAsync(saveAs: false), "Ctrl+S"));
        menu.Items.Add(MenuEntry("Save as…", () => _ = SaveAsync(saveAs: true), "Ctrl+Shift+S"));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Import CSV…", () => _ = OpenWithPickerAsync(csv: true), "Ctrl+I"));
        menu.Items.Add(MenuEntry("Export as PNG…", () => _ = ExportPngAsync(), "Ctrl+E"));
        if (Store is { } store)
        {
            menu.Items.Add(new Separator());
            if (store.IsPremium)
                menu.Items.Add(new MenuItem { Header = "Premium: no ads. Thank you!", IsEnabled = false });
            else
                menu.Items.Add(MenuEntry(store.Price is { } price ? $"Remove ads ({price})" : "Remove ads", () => _ = BuyPremiumAsync()));
#if DEBUG
            menu.Items.Add(MenuEntry(store.IsPremium ? "Debug: turn Premium off" : "Debug: pretend to have Premium",
                () => store.SimulatePremium(!store.IsPremium)));
#endif
        }
        var adChoices = PrivacyOptions is not null && !HasPremium;
        if (adChoices || PrivacyPolicy is not null) menu.Items.Add(new Separator());
        if (adChoices) menu.Items.Add(MenuEntry("Ad privacy choices…", PrivacyOptions!));
        if (PrivacyPolicy is { } policy)
            menu.Items.Add(MenuEntry("Privacy policy", () => _ = Top?.Launcher.LaunchUriAsync(policy)));
        menu.ShowAt((Control)sender!);
    }

    private async Task BuyPremiumAsync()
    {
        if (Store is null) return;
        if (await Store.PurchaseAsync() is { } message) await ShowMessageAsync(message);
    }

    private async Task NewBoardAsync()
    {
        if (!await ConfirmDiscardChangesAsync()) return;
        _board.Load(new BoardData());
        SetDocument(null, "Untitled");
        ResetView();
    }

    private async Task OpenWithPickerAsync(bool csv)
    {
        if (!await ConfirmDiscardChangesAsync() || Top?.StorageProvider is not { CanOpen: true } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = csv ? "Import a CSV export" : "Open a board",
            FileTypeFilter = [csv ? CsvFiles : BoardFiles, FilePickerFileTypes.All],
        });
        if (files.Count > 0) await OpenFileAsync(files[0]);
    }

    /// <summary>Opens a board or Miro CSV from a path, e.g. one given on the command line.</summary>
    public async Task OpenPathAsync(string path)
    {
        if (Top?.StorageProvider is { } storage && await storage.TryGetFileFromPathAsync(path) is { } file)
            await OpenFileAsync(file);
    }

    private async Task OpenFileAsync(IStorageFile file)
    {
        try
        {
            string content;
            await using (var stream = await file.OpenReadAsync())
            using (var reader = new StreamReader(stream))
                content = await reader.ReadToEndAsync();
            var (data, isCsv) = BoardFile.Read(file.Name, content);
            _board.Load(data);
            // An imported CSV has no board file yet, so the first save asks where to put it.
            SetDocument(isCsv ? null : file, Path.GetFileNameWithoutExtension(file.Name));
            if (!isCsv) await BookmarkDocumentAsync();
            FitBoard();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            await ShowMessageAsync($"Couldn't open {file.Name}.\n\n{ex.Message}");
        }
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        EndEditing();
        var file = _document;
        if (saveAs || file is null)
        {
            file = await PickSaveFileAsync(_docName, BoardSaveType, "storyboard");
            if (file is null) return false;
        }
        try
        {
            await WriteAsync(file, stream =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                writer.Write(BoardFile.Serialize(_board.ToData()));
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await ShowMessageAsync($"Couldn't save {file.Name}.\n\n{ex.Message}");
            return false;
        }
        _board.IsDirty = false;
        if (file != _document) SetDocument(file, Path.GetFileNameWithoutExtension(file.Name));
        await BookmarkDocumentAsync();
        UpdateTitle();
        SaveSession();
        return true;
    }

    private async Task<IStorageFile?> PickSaveFileAsync(string name, FilePickerFileType type, string extension)
    {
        if (Top?.StorageProvider is not { CanSave: true } storage) return null;
        return await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            // Without the extension: both the Windows dialog and Avalonia's Android picker add DefaultExtension.
            SuggestedFileName = name,
            DefaultExtension = extension,
            FileTypeChoices = [type],
            ShowOverwritePrompt = true,
        });
    }

    // Replaces the file's contents (desktop file streams don't truncate on their own).
    private static async Task WriteAsync(IStorageFile file, Action<Stream> write)
    {
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        write(stream);
    }

    /// <summary>Asks to save unsaved changes. False means the user cancelled.</summary>
    public async Task<bool> ConfirmDiscardChangesAsync()
    {
        EndEditing();
        if (!_board.IsDirty) return true;
        var answer = await ShowMessageAsync($"Save changes to \"{_docName}\"?", "Save", "Don't save", "Cancel");
        return answer switch
        {
            "Save" => await SaveAsync(saveAs: false),
            "Don't save" => true,
            _ => false,
        };
    }

    private void SetDocument(IStorageFile? file, string name, string? bookmark = null)
    {
        _document = file;
        _documentBookmark = bookmark;
        _docName = name;
        UpdateTitle();
    }

    // On phones the app may be closed in the background; a bookmark lets it reopen the file afterwards.
    private async Task BookmarkDocumentAsync()
    {
        if (!_isMobile || _document is not { CanBookmark: true } file) return;
        try
        {
            _documentBookmark = await file.SaveBookmarkAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _documentBookmark = null;
        }
    }

    private void UpdateTitle()
    {
        var dirty = _board.IsDirty ? " •" : "";
        DocumentTitle = $"{_docName}{dirty} – Story Boarder";
        DocTitle.Text = _docName + dirty;
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    // Dropped pictures land on the board where they were dropped; anything else is opened.
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles() is not { Length: > 0 } items) return;
        var files = items.OfType<IStorageFile>().ToList();
        var pictures = files.Where(f => IsPictureFile(f.Name)).ToList();
        if (pictures.Count > 0) await AddPictureFilesAsync(pictures, ToBoard(e.GetPosition(Viewport)));
        else if (files.Count > 0 && await ConfirmDiscardChangesAsync()) await OpenFileAsync(files[0]);
    }

    private async Task ExportPngAsync(bool wholeBoard = false)
    {
        EndEditing();
        var items = wholeBoard ? new List<BoardItemViewModel>() : _board.SelectionWithFrameContents();
        if (items.Count == 0) items = _board.Items.ToList();
        if (items.Count == 0) return;
        var file = await PickSaveFileAsync(_docName, PngFile, "png");
        if (file is null) return;
        try
        {
            using var bitmap = RenderPng(items);
            await WriteAsync(file, stream => bitmap.Save(stream, PngBitmapEncoderOptions.Default));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await ShowMessageAsync($"Couldn't export {file.Name}.\n\n{ex.Message}");
        }
    }

    // Renders the area around `items` at up to 2x, keeping the picture under MaxExportPixels on its long side.
    private RenderTargetBitmap RenderPng(IReadOnlyCollection<BoardItemViewModel> items)
    {
        var bounds = BoardViewModel.Union(items.Select(i => i.Bounds))!.Value;
        var scale = Math.Min(2, MaxExportPixels / Math.Max(bounds.Width, bounds.Height));
        // A margin all round, plus room for frame titles (which are enlarged when the picture is scaled down).
        var titleRoom = items.Any(i => i is FrameViewModel) ? 34 * Math.Clamp(1 / scale, 1, 10) : 0;
        var area = new Rect(bounds.X - 40, bounds.Y - 40 - titleRoom, bounds.Width + 80, bounds.Height + 80 + titleRoom);
        scale = Math.Min(2, MaxExportPixels / Math.Max(area.Width, area.Height));
        var size = new PixelSize((int)Math.Ceiling(area.Width * scale), (int)Math.Ceiling(area.Height * scale));

        // Point the view at the area, stretch the board layer over the whole picture with the board color behind
        // it (text antialiases badly onto transparency), hide selection outlines, render, then put it all back.
        var (zoom, panX, panY) = (_zoom, _panX, _panY);
        var selectedItems = _board.SelectedItems.ToList();
        var selectedConnectors = _board.Connectors.Where(c => c.IsSelected).ToList();
        _board.ClearSelection();
        var picture = new RenderTargetBitmap(size);
        try
        {
            BoardLayer.Width = size.Width;
            BoardLayer.Height = size.Height;
            BoardLayer.HorizontalAlignment = HorizontalAlignment.Left;
            BoardLayer.VerticalAlignment = VerticalAlignment.Top;
            BoardLayer.Background = (IBrush)Resources["BoardBrush"]!;
            SetView(scale, -area.X * scale, -area.Y * scale);
            BoardLayer.UpdateLayout();
            picture.Render(BoardLayer);
        }
        finally
        {
            BoardLayer.Width = BoardLayer.Height = double.NaN;
            BoardLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            BoardLayer.VerticalAlignment = VerticalAlignment.Stretch;
            BoardLayer.Background = null;
            SetView(zoom, panX, panY);
            foreach (var item in selectedItems) item.IsSelected = true;
            foreach (var connector in selectedConnectors) connector.IsSelected = true;
        }
        return picture;
    }

    // ---------- Phones and tablets: keeping work across app restarts ----------

    private static string SessionPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StoryBoarder", "session.json");

    private void OnAppDeactivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Background && _isMobile) SaveSession();
    }

    private void RestartAutosave()
    {
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    /// <summary>
    /// Keeps the open board (saved or not) in the app's private storage, so it's still there if Android closes
    /// the app in the background.
    /// </summary>
    private void SaveSession()
    {
        if (!_isMobile) return;
        try
        {
            var session = new SessionData
            {
                Board = _board.ToData(),
                DocName = _docName,
                FileBookmark = _document is not null ? _documentBookmark : null,
                IsDirty = _board.IsDirty,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(SessionPath)!);
            var temp = SessionPath + ".tmp";
            File.WriteAllText(temp, BoardFile.SerializeSession(session));
            File.Move(temp, SessionPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal: the board is still open; it just won't survive the app being closed.
        }
    }

    private async Task RestoreSessionAsync()
    {
        SessionData? session;
        try
        {
            if (!File.Exists(SessionPath)) return;
            session = BoardFile.DeserializeSession(await File.ReadAllTextAsync(SessionPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return;
        }
        if (session is null) return;

        _board.Load(session.Board);
        _board.IsDirty = session.IsDirty;
        IStorageFile? file = null;
        if (session.FileBookmark is { } bookmark && Top?.StorageProvider is { } storage)
        {
            try
            {
                file = await storage.OpenFileBookmarkAsync(bookmark);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // The file has gone or access was revoked; the next save asks where to put it.
            }
        }
        SetDocument(file, session.DocName, file is not null ? session.FileBookmark : null);
        FitBoard();
    }

    // ---------- Toolbar ----------

    private void Undo_Click(object? sender, RoutedEventArgs e) => WithEditEnded(_board.Undo);
    private void Redo_Click(object? sender, RoutedEventArgs e) => WithEditEnded(_board.Redo);
    private void AddNote_Click(object? sender, RoutedEventArgs e) => AddNoteAt(ToBoard(ViewportCenter));
    private void AddText_Click(object? sender, RoutedEventArgs e) => AddTextAt(ToBoard(ViewportCenter));
    private void AddFrame_Click(object? sender, RoutedEventArgs e) => AddFrameAt(ToBoard(ViewportCenter));
    private void AddImage_Click(object? sender, RoutedEventArgs e) => _ = AddPicturesWithPickerAsync(ToBoard(ViewportCenter));

    // Finishes text editing first, so the edit lands in undo history before the action.
    private void WithEditEnded(Action action)
    {
        EndEditing();
        action();
    }

    private void Swatch_Click(object? sender, RoutedEventArgs e)
    {
        if ((e.Source as Control)?.DataContext is not PaletteColor color) return;
        ApplyColor(color.Hex);
        // Close the color flyout the swatch was in, if any.
        foreach (var button in new[] { ToolbarColorButton, CompactColorButton, SelectionColorButton }) button.Flyout?.Hide();
    }

    private void ApplyColor(string hex)
    {
        EndEditing();
        _noteColor = hex;
        UpdateNoteColorChip();
        _board.Change(() => _board.Recolor(hex));
    }

    private void UpdateNoteColorChip() =>
        NoteColorChip.Background = CompactNoteColorChip.Background = Converters.BrushFor(_noteColor);

    private void Frames_Click(object? sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var frame in _board.FramesInReadingOrder())
        {
            // A TextBlock header, so underscores in frame names aren't taken as access keys.
            var title = string.IsNullOrWhiteSpace(frame.Text) ? "(untitled frame)" : frame.Text;
            var item = new MenuItem { Header = new TextBlock { Text = title, MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis } };
            item.Click += (_, _) =>
            {
                _board.SelectOnly(frame);
                ZoomToItem(frame);
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No frames yet", IsEnabled = false });
        menu.ShowAt((Control)sender!);
    }

    // ---------- Search ----------

    private TextBox ActiveSearchBox => CompactSearch.IsVisible ? CompactSearchBox : SearchBox;

    private void FocusSearch()
    {
        if (Classes.Contains("compact")) CompactSearch.IsVisible = true;
        ActiveSearchBox.Focus();
        ActiveSearchBox.SelectAll();
    }

    private void SearchToggle_Click(object? sender, RoutedEventArgs e)
    {
        if (CompactSearch.IsVisible) CloseSearch();
        else FocusSearch();
    }

    private void CloseSearch()
    {
        CompactSearchBox.Text = "";
        SearchBox.Text = "";
        CompactSearch.IsVisible = false;
        Viewport.Focus();
    }

    // Typing highlights every match; Enter steps through them one at a time.
    private void Search_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = ((TextBox)sender!).Text ?? "";
        _matches = _board.Find(text);
        _match = -1;
        _board.Select(_matches);
        SetSearchCount(text.Trim().Length == 0 ? "" : $"{_matches.Count} found");
    }

    private void SetSearchCount(string text) => SearchCount.Text = CompactSearchCount.Text = text;

    private void ShowMatch(int step)
    {
        _matches = _board.Find(ActiveSearchBox.Text ?? ""); // the board may have changed since the last keystroke
        if (_matches.Count == 0) return;
        _match = _match < 0 ? (step > 0 ? 0 : _matches.Count - 1) : (_match + step + _matches.Count) % _matches.Count;
        var item = _matches[_match];
        _board.SelectOnly(item);
        ZoomToItem(item);
        SetSearchCount($"{_match + 1} of {_matches.Count}");
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

    private async Task AddPicturesWithPickerAsync(Point position)
    {
        EndEditing();
        if (Top?.StorageProvider is not { CanOpen: true } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add pictures",
            AllowMultiple = true,
            FileTypeFilter = [PictureFiles],
        });
        if (files.Count > 0) await AddPictureFilesAsync(files, position);
    }

    private async Task AddPictureFilesAsync(IEnumerable<IStorageFile> files, Point position)
    {
        var pictures = new List<byte[]>();
        var failed = 0;
        foreach (var file in files)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory);
                pictures.Add(memory.ToArray());
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }
        await AddPicturesAsync(pictures, position, failed);
    }

    private async Task AddPicturesAsync(IEnumerable<byte[]> pictures, Point position, int failed = 0)
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
            catch (FormatException)
            {
                failed++;
                continue;
            }
            var image = ImageStore.Get(id)!.PixelSize;
            var scale = Math.Min(1, MaxPictureSize / Math.Max(image.Width, image.Height));
            var offset = added.Count * 40; // fan several pictures out so they don't stack exactly
            added.Add(_board.AddImage(id, position + new Vector(offset, offset), image.Width * scale, image.Height * scale));
        }
        _board.Select(added);
        _board.Commit(before);
        if (failed > 0)
            await ShowMessageAsync(failed == 1 ? "A file couldn't be read as a picture." : $"{failed} files couldn't be read as pictures.");
    }

    private void BeginEdit(BoardItemViewModel item, string? before = null)
    {
        EndEditing();
        _board.SelectOnly(item);
        if (item is ImageViewModel) return; // pictures have no text
        _editBefore = before ?? _board.Snapshot();
        item.IsEditing = true;
        // Focus the text box once it has appeared.
        Dispatcher.UIThread.Post(() =>
        {
            if (!item.IsEditing || EditBoxFor(item) is not { } box) return;
            box.Focus();
            box.CaretIndex = box.Text?.Length ?? 0;
            Dispatcher.UIThread.Post(KeepEditedItemVisible, DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    private TextBox? EditBoxFor(BoardItemViewModel item) =>
        BoardItems.ContainerFromItem(item)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("edit"));

    private void EditSelection()
    {
        if (_board.SelectedItems.ToList() is [var only]) BeginEdit(only);
    }

    // Moving keyboard focus off the edit box ends editing (see OnLostFocus).
    private void EndEditing()
    {
        if (_board.Items.Any(i => i.IsEditing)) Viewport.Focus();
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: BoardItemViewModel item } box || !box.Classes.Contains("edit")) return;
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

    // On phones, the keyboard can cover the note being edited; pan it into view.
    private void OnInputPaneChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (e.NewState == InputPaneState.Open) Dispatcher.UIThread.Post(KeepEditedItemVisible, DispatcherPriority.Background);
    }

    private void KeepEditedItemVisible()
    {
        if (_board.Items.FirstOrDefault(i => i.IsEditing) is not { } item || Top is not { InputPane: { } pane } top) return;
        var occluded = pane.OccludedRect;
        if (pane.State != InputPaneState.Open || occluded.Height <= 0) return;
        if (Viewport.TranslatePoint(new Point(0, 0), top) is not { } origin) return;
        var visibleBottom = Math.Min(Viewport.Bounds.Height, occluded.Top - origin.Y) - 16;
        var screen = Adorner.ToScreen(item.Bounds);
        if (screen.Bottom <= visibleBottom) return;
        // Move the item up, but never so far that its top goes off screen.
        var shift = Math.Min(screen.Bottom - visibleBottom, screen.Top - 16);
        if (shift > 0) SetView(_zoom, _panX, _panY - shift);
    }

    // ---------- Copy and paste ----------

    private async Task<bool> CopySelectionAsync()
    {
        var clip = _board.CopySelection();
        if (clip.Items.Count == 0 || Top?.Clipboard is not { } clipboard) return false;
        var json = BoardFile.Serialize(clip);
        // Plain text too, so copied notes can be pasted into other apps.
        var text = string.Join("\n\n", clip.Items.Select(i => i.Text).Where(t => t.Length > 0));
        _lastCopy = (json, text);
        try
        {
            var item = new DataTransferItem();
            item.Set(ItemsFormat, json);
            if (text.Length > 0) item.SetText(text);
            var data = new DataTransfer();
            data.Add(item);
            await clipboard.SetDataAsync(data);
        }
        catch (Exception)
        {
            // Some platforms only take text; the board items still paste from the in-app copy.
            try
            {
                await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync($"Couldn't copy to the clipboard.\n\n{ex.Message}");
                return false;
            }
        }
        return true;
    }

    private async Task CutSelectionAsync()
    {
        if (await CopySelectionAsync()) _board.Change(_board.DeleteSelected);
    }

    // Pastes copied board items, a picture, picture files or text (as a sticky note), in that order of preference.
    private async Task PasteAsync(Point position)
    {
        EndEditing();
        if (Top?.Clipboard is not { } clipboard) return;
        try
        {
            var text = await clipboard.TryGetTextAsync();
            var json = await TryGet(() => clipboard.TryGetValueAsync(ItemsFormat));
            if (json is null && _lastCopy is { } last && (text ?? "") == last.Text) json = last.Json;
            if (json is not null)
            {
                _board.Change(() => _board.Paste(BoardFile.Deserialize(json), position));
            }
            else if (await TryGet(clipboard.TryGetBitmapAsync) is { } bitmap)
            {
                using var png = new MemoryStream();
                bitmap.Save(png, PngBitmapEncoderOptions.Default);
                await AddPicturesAsync(new[] { png.ToArray() }, position);
            }
            else if (await TryGet(clipboard.TryGetFilesAsync) is { } items
                     && items.OfType<IStorageFile>().Where(f => IsPictureFile(f.Name)).ToList() is { Count: > 0 } pictures)
            {
                await AddPictureFilesAsync(pictures, position);
            }
            else if (text?.Trim() is { Length: > 0 } trimmed)
            {
                AddTextNote(trimmed, position);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            await ShowMessageAsync($"Couldn't paste that.\n\n{ex.Message}");
        }
    }

    // Clipboard formats a platform doesn't support come back as errors; treat them as "not there".
    private static async Task<T?> TryGet<T>(Func<Task<T?>> get) where T : class
    {
        try
        {
            return await get();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    // ---------- Pointer input: mouse, pen and touch ----------

    private void SetTouchMode(bool touch)
    {
        if (_touchMode == touch && HintText.Text is not null) return;
        _touchMode = touch;
        Classes.Set("touch", touch);
        Adorner.TouchMode = touch;
        Adorner.InvalidateVisual();
        HintText.Text = touch
            ? "Drag to move notes or the board  ·  Pinch to zoom  ·  Double-tap: new note or edit  ·  Long-press: more"
            : "Drag frames and notes to move them  ·  Drag empty space, Space+drag or middle-drag: pan  ·  Shift+drag: select  ·  Scroll: zoom  ·  Drag a selected note's dots: draw an arrow  ·  Right-click: more";
        EmptyHint.Text = touch
            ? "Import a whiteboard exported as CSV, or double-tap anywhere to add a sticky note."
            : "Import a whiteboard exported as CSV, drop a .csv file here, or double-click anywhere to add a sticky note.";
        UpdateSelectionBar();
    }

    private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Viewport);
        var position = point.Position;
        var isTouch = e.Pointer.Type == PointerType.Touch;
        SetTouchMode(isTouch);
        _fitOnResize = false;

        // Presses in the edit box belong to it: they place the caret and select text.
        if (_touches.Count == 0 && (IsInTextEditor(e.Source) || IsOnEditedFrameTitle(position))) return;

        // Before capturing the pointer: capture ends the hover that shows a mouse user the arrow dots.
        var handle = Adorner.HandleAt(position);

        if (isTouch)
        {
            _touches[e.Pointer] = position;
            e.Pointer.Capture(Viewport);
            if (_touches.Count >= 2)
            {
                if (_touches.Count == 2) StartPinch();
                e.Handled = true;
                return;
            }
        }

        EndEditing();
        var props = point.Properties;
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var (target, element) = HitTarget(e.Source, position);
        e.Handled = true;

        if (props.IsRightButtonPressed)
        {
            _rightPress = position;
            _pressTarget = element;
            return;
        }
        // Middle-drag, or left-drag while holding Space, pans from anywhere, even on top of notes.
        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && _spaceHeld))
        {
            StartPointerGesture(e.Pointer, position, isTouch, null);
            StartPan(position);
            return;
        }
        if (!props.IsLeftButtonPressed) return;

        StartPointerGesture(e.Pointer, position, isTouch, element);
        var boardPoint = ToBoard(position);

        if (handle is { } h)
        {
            if (h.Kind == HandleKind.Resize) StartResize(h.Item);
            else StartConnect(h.Item);
            return;
        }

        switch (target)
        {
            case HitKind.Item or HitKind.FrameTitle:
                var item = (BoardItemViewModel)element!;
                if (e.ClickCount == 2) BeginEditAndEndGesture(item);
                else if (SelectForDrag(item, ctrl)) Pend(Gesture.Move);
                else EndGesture();
                break;

            case HitKind.FrameBody when e.ClickCount == 2:
            case HitKind.None when e.ClickCount == 2:
                EndGesture();
                AddNoteAt(boardPoint);
                break;

            case HitKind.FrameBody or HitKind.None when shift || SelectAreaToggle.IsChecked == true:
                _gesture = Gesture.BoxSelect;
                _clearOnTap = false;
                break;

            case HitKind.FrameBody:
                // Dragging a frame's body moves the frame and everything in it.
                if (SelectForDrag((FrameViewModel)element!, ctrl)) Pend(Gesture.Move);
                else EndGesture();
                break;

            case HitKind.Connector:
                var connector = (ConnectorViewModel)element!;
                if (ctrl) connector.IsSelected = !connector.IsSelected;
                else _board.SelectOnly(connector);
                Pend(Gesture.Pan);
                break;

            default:
                // Empty board: a drag pans; a tap (no drag) clears the selection.
                _clearOnTap = !ctrl;
                Pend(Gesture.Pan);
                break;
        }
    }

    private void BeginEditAndEndGesture(BoardItemViewModel item)
    {
        EndGesture();
        BeginEdit(item);
    }

    private void StartPointerGesture(IPointer pointer, Point position, bool isTouch, object? target)
    {
        _pointer = pointer;
        _pressPoint = position;
        _pressIsTouch = isTouch;
        _pressTarget = target;
        _clearOnTap = false;
        pointer.Capture(Viewport);
    }

    private void Pend(Gesture becomes)
    {
        _gesture = Gesture.Pending;
        _pendingAs = becomes;
        // On touch screens a long press opens the menu that right-click opens with a mouse.
        if (_pressIsTouch) _holdTimer.Start();
    }

    // Selects the item (Ctrl toggles it instead). True if it ends up selected, so it can be dragged.
    private bool SelectForDrag(BoardItemViewModel item, bool ctrl)
    {
        if (ctrl) item.IsSelected = !item.IsSelected;
        else if (!item.IsSelected) _board.SelectOnly(item);
        return item.IsSelected;
    }

    private void Viewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(Viewport);
        if (_touches.ContainsKey(e.Pointer))
        {
            _touches[e.Pointer] = position;
            if (_gesture == Gesture.Pinch)
            {
                UpdatePinch();
                return;
            }
        }
        if (_gesture == Gesture.None)
        {
            if (e.Pointer.Type != PointerType.Touch) UpdateHover(position);
            return;
        }
        if (e.Pointer == _pointer) ContinueGesture(position);
    }

    private void ContinueGesture(Point position)
    {
        switch (_gesture)
        {
            case Gesture.Pending:
                if (Point.Distance(position, _pressPoint) < (_pressIsTouch ? TouchSlop : MouseSlop)) return;
                _holdTimer.Stop();
                _clearOnTap = false;
                if (_pendingAs == Gesture.Move)
                {
                    _dragBefore = _board.Snapshot();
                    _board.BeginMove();
                    _lastBoard = ToBoard(_pressPoint);
                    _gesture = Gesture.Move;
                }
                else
                {
                    StartPan(_pressPoint);
                }
                ContinueGesture(position);
                break;

            case Gesture.Pan:
                SetView(_zoom, _panOrigin.X + position.X - _panStart.X, _panOrigin.Y + position.Y - _panStart.Y);
                break;

            case Gesture.Move:
                var point = ToBoard(position);
                _board.MoveBy(point.X - _lastBoard.X, point.Y - _lastBoard.Y);
                _lastBoard = point;
                break;

            case Gesture.Resize when _resizeItem is { } item:
                var start = ToBoard(_pressPoint);
                var now = ToBoard(position);
                var width = Math.Max(item.MinSize, _resizeStart.Width + now.X - start.X);
                var height = Math.Max(item.MinSize, _resizeStart.Height + now.Y - start.Y);
                if (item is ImageViewModel) height = width * _resizeStart.Height / _resizeStart.Width; // pictures keep their shape
                item.Width = width;
                item.Height = height;
                break;

            case Gesture.Connect:
                UpdateConnect(ToBoard(position));
                break;

            case Gesture.BoxSelect:
                Adorner.SelectionBox = new Rect(_pressPoint, position).Normalize();
                Adorner.InvalidateVisual();
                break;
        }
    }

    private void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var position = e.GetPosition(Viewport);
        if (_touches.Remove(e.Pointer) && _gesture == Gesture.Pinch)
        {
            // Lifting one finger of a pinch carries on as a pan with the other.
            if (_touches.Count == 1)
            {
                var (pointer, at) = _touches.First();
                _pointer = pointer;
                StartPan(at);
            }
            else if (_touches.Count >= 2)
            {
                StartPinch();
            }
            else
            {
                EndGesture();
            }
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Right && _rightPress is { } pressed)
        {
            _rightPress = null;
            OpenContextMenu(pressed, _pressTarget);
            return;
        }
        if (e.Pointer != _pointer) return;
        FinishGesture(position, canceled: false);
    }

    private void Viewport_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Elements inside the board lose their implicit capture when the board takes it; only the board's own loss counts.
        if (!ReferenceEquals(e.Source, Viewport)) return;
        _touches.Remove(e.Pointer);
        if (e.Pointer == _pointer && _gesture != Gesture.None) FinishGesture(_pressPoint, canceled: true);
    }

    private void FinishGesture(Point position, bool canceled)
    {
        var gesture = _gesture;
        var clearOnTap = _clearOnTap;
        var pointer = _pointer;
        EndGesture();
        switch (gesture)
        {
            case Gesture.Pending when clearOnTap && !canceled:
                _board.ClearSelection();
                break;
            case Gesture.Move or Gesture.Resize:
                FinishDrag();
                break;
            case Gesture.Connect:
                FinishConnect(ToBoard(position), canceled);
                break;
            case Gesture.BoxSelect when !canceled:
                var area = new Rect(ToBoard(_pressPoint), ToBoard(position)).Normalize();
                _board.SelectWithin(area, addToSelection: false);
                break;
        }
        pointer?.Capture(null);
    }

    private void EndGesture()
    {
        _gesture = Gesture.None;
        _pointer = null;
        _resizeItem = null;
        _clearOnTap = false;
        _holdTimer.Stop();
        Viewport.Cursor = null;
        if (Adorner.SelectionBox is not null)
        {
            Adorner.SelectionBox = null;
            Adorner.InvalidateVisual();
        }
    }

    private void FinishDrag()
    {
        if (_dragBefore is not { } before) return;
        _dragBefore = null;
        _board.Commit(before);
    }

    private void StartPan(Point position)
    {
        _gesture = Gesture.Pan;
        _panStart = position;
        _panOrigin = new Point(_panX, _panY);
        Viewport.Cursor = _panCursor;
    }

    private void StartResize(BoardItemViewModel item)
    {
        _gesture = Gesture.Resize;
        _resizeItem = item;
        _resizeStart = new Size(item.Width, item.Height);
        _dragBefore = _board.Snapshot();
    }

    // Two fingers zoom around the point between them and pan as they move together.
    private void StartPinch()
    {
        // Whatever the first finger was doing ends here; a drag that hadn't started yet never happens.
        if (_gesture is Gesture.Move or Gesture.Resize) FinishDrag();
        if (_gesture == Gesture.Connect) FinishConnect(_pressPoint, canceled: true);
        _holdTimer.Stop();
        _clearOnTap = false;
        Adorner.SelectionBox = null;

        var (a, b) = FirstTwoTouches();
        _gesture = Gesture.Pinch;
        _pinchDistance = Math.Max(1, Point.Distance(a, b));
        _pinchZoom = _zoom;
        _pinchAnchor = ToBoard(Midpoint(a, b));
    }

    private void UpdatePinch()
    {
        var (a, b) = FirstTwoTouches();
        var scale = Math.Clamp(_pinchZoom * Point.Distance(a, b) / _pinchDistance, MinZoom, MaxZoom);
        var middle = Midpoint(a, b);
        SetView(scale, middle.X - _pinchAnchor.X * scale, middle.Y - _pinchAnchor.Y * scale);
    }

    private (Point, Point) FirstTwoTouches()
    {
        var points = _touches.Values.Take(2).ToList();
        return (points[0], points[1]);
    }

    private static Point Midpoint(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    // A long press that hasn't moved opens the context menu.
    private void OnHold()
    {
        _holdTimer.Stop();
        if (_gesture != Gesture.Pending) return;
        var (point, target, pointer) = (_pressPoint, _pressTarget, _pointer);
        EndGesture();
        if (pointer is not null) _touches.Remove(pointer); // its release goes to the menu, not here
        pointer?.Capture(null);
        OpenContextMenu(point, target);
    }

    private void UpdateHover(Point position)
    {
        var handle = Adorner.HandleAt(position);
        Viewport.Cursor = handle?.Kind switch
        {
            HandleKind.Resize => _resizeCursor,
            HandleKind.Connect => _connectCursor,
            _ when ConnectorAt(ToBoard(position)) is not null => _handCursor,
            _ => null,
        };
        // A selected item shows its arrow dots while the mouse is over it (or over the dots, just outside).
        var hover = handle?.Item ?? _board.SelectedItems.OrderByDescending(i => i.ZIndex)
            .FirstOrDefault(i => Adorner.ToScreen(i.Bounds).Inflate(12).Contains(position));
        SetHover(hover);
    }

    private void SetHover(BoardItemViewModel? item)
    {
        if (Adorner.HoverItem == item) return;
        Adorner.HoverItem = item;
        Adorner.InvalidateVisual();
    }

    private enum HitKind { None, Item, FrameTitle, FrameBody, Connector }

    // What's under the pointer, top-most first: notes, text and pictures; arrows; frame titles; frame bodies.
    // Items are found from the element the pointer went down on (their template parts are tagged with classes
    // in MainView.axaml). Arrows and frame titles draw outside their layout boxes, which Avalonia doesn't
    // hit-test, so those are checked against their shapes instead.
    private (HitKind, object?) HitTarget(object? source, Point position)
    {
        var tagged = (HitKind.None, (object?)null);
        for (var visual = source as Visual; visual is not null && visual != Viewport; visual = visual.GetVisualParent())
        {
            if (visual is not StyledElement element) continue;
            if (element.Classes.Contains("item")) return (HitKind.Item, element.DataContext);
            if (element.Classes.Contains("connector")) return (HitKind.Connector, element.DataContext);
            if (element.Classes.Contains("frame-title")) tagged = (HitKind.FrameTitle, element.DataContext);
            else if (element.Classes.Contains("frame-body") && tagged.Item1 == HitKind.None) tagged = (HitKind.FrameBody, element.DataContext);
        }
        var point = ToBoard(position);
        if (ConnectorAt(point) is { } connector) return (HitKind.Connector, connector);
        if (tagged.Item1 != HitKind.FrameTitle && FrameTitleAt(point) is { } frame) return (HitKind.FrameTitle, frame);
        return tagged;
    }

    private ConnectorViewModel? ConnectorAt(Point board) =>
        _board.Connectors.LastOrDefault(c =>
            c.Geometry is { } geometry && geometry.StrokeContains(new Pen(Brushes.Black, c.HitThickness), board));

    private FrameViewModel? FrameTitleAt(Point board) =>
        _board.Items.OfType<FrameViewModel>().OrderByDescending(f => f.ZIndex)
            .FirstOrDefault(f => FrameTitleBounds(f) is { } title && title.Contains(board));

    // The title sits 4 units above the frame, 26 tall, scaled up from its bottom-left corner (see MainView.axaml).
    private Rect? FrameTitleBounds(FrameViewModel frame)
    {
        if (BoardItems.ContainerFromItem(frame)?.GetVisualDescendants().OfType<Panel>()
                .FirstOrDefault(p => p.Classes.Contains("frame-title")) is not { } title) return null;
        var scale = TitleScale;
        var width = frame.IsEditing ? Math.Max(title.Bounds.Width, 180) : title.Bounds.Width;
        return new Rect(frame.X, frame.Y - 4 - 26 * scale, width * scale, 26 * scale);
    }

    private static bool IsInTextEditor(object? source) =>
        source is Visual visual && (visual as TextBox ?? visual.FindAncestorOfType<TextBox>()) is { IsVisible: true } box
        && box.Classes.Contains("edit");

    // A frame title's edit box lies outside the frame, where it can't be clicked, so presses there just keep editing.
    private bool IsOnEditedFrameTitle(Point position) =>
        _board.Items.OfType<FrameViewModel>().FirstOrDefault(f => f.IsEditing) is { } frame
        && FrameTitleBounds(frame) is { } title && title.Contains(ToBoard(position));

    private void OnBoardLayoutChanged()
    {
        Adorner.InvalidateVisual();
        UpdateSelectionBar();
    }

    private void UpdateSelectionBar()
    {
        var editing = _board.Items.Any(i => i.IsEditing);
        SelectionBar.IsVisible = _touchMode && !editing && _board.HasSelection;
        SelectionEdit.IsVisible = _board.SelectedItems.ToList() is [not ImageViewModel];
    }

    // ---------- Drawing arrows ----------

    private void StartConnect(BoardItemViewModel from)
    {
        _gesture = Gesture.Connect;
        _connectFrom = from;
        ConnectPreview.StrokeThickness = ConnectorViewModel.LineWidth * _board.LineScale;
        ConnectPreview.Data = null;
        ConnectPreview.IsVisible = true;
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
        ConnectPreview.IsVisible = false;
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

    // ---------- Context menus ----------

    private void OpenContextMenu(Point screen, object? target)
    {
        _contextPoint = ToBoard(screen);
        var menu = new ContextMenu
        {
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            PlacementRect = new Rect(screen, new Size(1, 1)),
        };
        switch (target)
        {
            case BoardItemViewModel item:
                if (!item.IsSelected) _board.SelectOnly(item);
                FillItemMenu(menu.Items);
                break;
            case ConnectorViewModel connector:
                if (!connector.IsSelected) _board.SelectOnly(connector);
                menu.Items.Add(MenuEntry("Reverse direction", () => _board.Change(_board.ReverseSelectedConnectors)));
                menu.Items.Add(MenuEntry("Delete", () => _board.Change(_board.DeleteSelected), "Delete"));
                break;
            default:
                menu.Items.Add(MenuEntry("Paste here", () => _ = PasteAsync(_contextPoint), "Ctrl+V"));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuEntry("Add sticky note here", () => AddNoteAt(_contextPoint)));
                menu.Items.Add(MenuEntry("Add text here", () => AddTextAt(_contextPoint), "T"));
                menu.Items.Add(MenuEntry("Add frame here", () => AddFrameAt(_contextPoint), "F"));
                menu.Items.Add(MenuEntry("Add picture here…", () => _ = AddPicturesWithPickerAsync(_contextPoint)));
                menu.Items.Add(new Separator());
                menu.Items.Add(MenuEntry("Select all", _board.SelectAll, "Ctrl+A"));
                menu.Items.Add(MenuEntry("Zoom to fit", ZoomToFit, "Ctrl+0"));
                menu.Items.Add(MenuEntry("Export board as PNG…", () => _ = ExportPngAsync(wholeBoard: true)));
                break;
        }
        menu.Open(Viewport);
    }

    // Shared by all items; actions apply to the selection.
    private void FillItemMenu(ItemCollection items)
    {
        items.Add(MenuEntry("Edit text", EditSelection, "Enter"));
        var colors = new MenuItem { Header = "Color" };
        foreach (var color in Palette.Notes)
        {
            var swatch = new MenuItem
            {
                Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new Border
                        {
                            Width = 16, Height = 16, CornerRadius = new CornerRadius(3),
                            Background = Converters.BrushFor(color.Hex),
                            BorderBrush = Converters.BrushFor("#33000000"), BorderThickness = new Thickness(1),
                        },
                        new TextBlock { Text = color.Name, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            swatch.Click += (_, _) => ApplyColor(color.Hex);
            colors.Items.Add(swatch);
        }
        items.Add(colors);
        items.Add(new Separator());
        items.Add(MenuEntry("Copy", () => _ = CopySelectionAsync(), "Ctrl+C"));
        items.Add(MenuEntry("Cut", () => _ = CutSelectionAsync(), "Ctrl+X"));
        items.Add(MenuEntry("Duplicate", () => _board.Change(_board.DuplicateSelection), "Ctrl+D"));
        items.Add(new Separator());
        items.Add(MenuEntry("Bring to front", () => _board.Change(_board.BringToFront)));
        items.Add(MenuEntry("Send to back", () => _board.Change(_board.SendToBack)));
        items.Add(new Separator());
        items.Add(MenuEntry("Add sticky note here", () => AddNoteAt(_contextPoint)));
        items.Add(MenuEntry("Paste here", () => _ = PasteAsync(_contextPoint)));
        items.Add(MenuEntry("Export selection as PNG…", () => _ = ExportPngAsync()));
        items.Add(new Separator());
        items.Add(MenuEntry("Delete", () => _board.Change(_board.DeleteSelected), "Delete"));
    }

    private MenuItem MenuEntry(string header, Action action, string? shortcut = null)
    {
        var item = new MenuItem { Header = header };
        // Keyboard shortcuts mean nothing on a touch screen.
        if (shortcut is not null && !_isMobile) item.InputGesture = KeyGesture.Parse(shortcut);
        item.Click += (_, _) => action();
        return item;
    }

    private void MenuEdit_Click(object? sender, RoutedEventArgs e) => EditSelection();
    private void MenuDuplicate_Click(object? sender, RoutedEventArgs e) => _board.Change(_board.DuplicateSelection);
    private void MenuDelete_Click(object? sender, RoutedEventArgs e) => _board.Change(_board.DeleteSelected);

    private void SelectionMore_Click(object? sender, RoutedEventArgs e)
    {
        var button = (Control)sender!;
        var target = (object?)_board.SelectedItems.LastOrDefault() ?? _board.Connectors.FirstOrDefault(c => c.IsSelected);
        _contextPoint = _board.SelectedItems.LastOrDefault()?.Center ?? ToBoard(ViewportCenter);
        var corner = button.TranslatePoint(new Point(0, 0), Viewport) ?? ViewportCenter;
        OpenContextMenu(corner, target);
    }

    // ---------- Panning and zooming ----------

    private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        ZoomAt(e.GetPosition(Viewport), Math.Pow(1.2, e.Delta.Y));
        e.Handled = true;
    }

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => ZoomAt(ViewportCenter, 1.25);
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => ZoomAt(ViewportCenter, 0.8);
    private void ZoomFit_Click(object? sender, RoutedEventArgs e) => ZoomToFit();

    private Point ViewportCenter => new(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2);

    private Point ToBoard(Point screen) => new((screen.X - _panX) / _zoom, (screen.Y - _panY) / _zoom);

    private Point PointerOrCenter(Point? pointer) =>
        ToBoard(pointer is { } p && new Rect(Viewport.Bounds.Size).Contains(p) ? p : ViewportCenter);

    private Point? _lastPointer;
    private bool _fitOnResize; // the board was just opened and nobody has touched the view yet

    // Fits a newly opened board in view, and keeps it fitted while the view is still settling into its size
    // (on phones it can resize a few times at startup), until the user presses on the board or zooms.
    private void FitBoard()
    {
        _fitOnResize = true;
        ZoomToFit();
    }

    // Zooms while keeping the board point under `screen` fixed.
    private void ZoomAt(Point screen, double factor)
    {
        _fitOnResize = false;
        var anchor = ToBoard(screen);
        var scale = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        SetView(scale, screen.X - anchor.X * scale, screen.Y - anchor.Y * scale);
    }

    private void ZoomToFit()
    {
        var (width, height) = (Viewport.Bounds.Width, Viewport.Bounds.Height);
        if (_board.Extent() is not { } extent || width < 1)
        {
            ResetView();
            return;
        }
        // Leave room around the content, plus extra on top for frame titles.
        var area = new Rect(extent.X - 60, extent.Y - 140, extent.Width + 120, extent.Height + 200);
        var scale = Math.Clamp(Math.Min(width / area.Width, height / area.Height), MinZoom, 1);
        SetView(scale, (width - area.Width * scale) / 2 - area.X * scale, (height - area.Height * scale) / 2 - area.Y * scale);
    }

    // Frames fill most of the view; smaller items come up to a readable zoom without filling the screen.
    private void ZoomToItem(BoardItemViewModel item)
    {
        var (width, height) = (Viewport.Bounds.Width, Viewport.Bounds.Height);
        var fit = Math.Min(width * 0.8 / item.Width, height * 0.8 / item.Height);
        var scale = item is FrameViewModel ? Math.Min(fit, 1.5) : Math.Min(fit, Math.Max(_zoom, 0.8));
        scale = Math.Clamp(scale, MinZoom, MaxZoom);
        var center = item.Center;
        SetView(scale, width / 2 - center.X * scale, height / 2 - center.Y * scale);
    }

    private void ResetView() => SetView(1, 0, 0);

    private void SetView(double scale, double panX, double panY)
    {
        (_zoom, _panX, _panY) = (scale, panX, panY);
        OnViewChanged();
    }

    private void OnViewChanged()
    {
        var view = new Matrix(_zoom, 0, 0, _zoom, _panX, _panY);
        var transform = new MatrixTransform(view);
        BoardItems.RenderTransform = transform;
        Overlay.RenderTransform = transform;
        if (GridLayer.Fill is DrawingBrush grid) grid.Transform = transform;
        // The dot grid turns to noise when zoomed far out.
        GridLayer.IsVisible = _zoom >= 0.3;

        ZoomText.Text = $"{_zoom * 100:0}%";
        var titleScale = Math.Clamp(1 / _zoom, 1, 10);
        if (titleScale != TitleScale)
        {
            TitleScale = titleScale;
            TitleTransform = new ScaleTransform(titleScale, titleScale);
        }
        _board.LineScale = titleScale;
        Adorner.View = view;
        Adorner.InvalidateVisual();
    }

    // ---------- Keyboard ----------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DialogLayer.IsVisible)
        {
            if (e.Key == Key.Escape) CloseDialog(null);
            else if (e.Key == Key.Enter && DialogButtons.Children.FirstOrDefault() is Button { Content: string first }) CloseDialog(first);
            e.Handled = true;
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var focused = Top?.FocusManager?.GetFocusedElement();

        if (focused is TextBox box)
        {
            if (box.Classes.Contains("search"))
            {
                if (e.Key == Key.Escape) CloseSearch();
                else if (e.Key == Key.Enter) ShowMatch(shift ? -1 : 1);
                else if (!ctrl || e.Key is not (Key.S or Key.F)) return;
            }
            else if (box.Classes.Contains("edit")
                     && (e.Key == Key.Escape || (e.Key == Key.Enter && (!box.AcceptsReturn || ctrl))))
            {
                EndEditing();
                e.Handled = true;
                return;
            }
            // While typing, only Save and Search reach the board.
            if (ctrl && e.Key == Key.S) _ = SaveAsync(saveAs: shift);
            else if (ctrl && e.Key == Key.F) FocusSearch();
            else if (!box.Classes.Contains("search")) return;
            e.Handled = true;
            return;
        }
        if (focused is MenuItem) return;

        var arrow = e.Key is Key.Left or Key.Right or Key.Up or Key.Down;
        if (!arrow) FinishNudge();

        e.Handled = true;
        switch (e.Key)
        {
            case Key.N when ctrl: _ = NewBoardAsync(); break;
            case Key.O when ctrl: _ = OpenWithPickerAsync(csv: false); break;
            case Key.I when ctrl: _ = OpenWithPickerAsync(csv: true); break;
            case Key.S when ctrl: _ = SaveAsync(saveAs: shift); break;
            case Key.E when ctrl: _ = ExportPngAsync(); break;
            case Key.F when ctrl: FocusSearch(); break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl: _board.Redo(); break;
            case Key.Z when ctrl: _board.Undo(); break;
            case Key.A when ctrl: _board.SelectAll(); break;
            case Key.C when ctrl: _ = CopySelectionAsync(); break;
            case Key.X when ctrl: _ = CutSelectionAsync(); break;
            case Key.V when ctrl: _ = PasteAsync(PointerOrCenter(_lastPointer)); break;
            case Key.D when ctrl: _board.Change(_board.DuplicateSelection); break;
            case Key.D0 or Key.NumPad0 when ctrl: ZoomToFit(); break;
            case Key.OemPlus or Key.Add when ctrl: ZoomAt(ViewportCenter, 1.25); break;
            case Key.OemMinus or Key.Subtract when ctrl: ZoomAt(ViewportCenter, 0.8); break;
            case Key.Delete or Key.Back: _board.Change(_board.DeleteSelected); break;
            case Key.Enter: EditSelection(); break;
            case Key.Escape: _board.ClearSelection(); break;
            case Key.N when !ctrl: AddNoteAt(PointerOrCenter(_lastPointer)); break;
            case Key.T when !ctrl: AddTextAt(PointerOrCenter(_lastPointer)); break;
            case Key.F when !ctrl: AddFrameAt(PointerOrCenter(_lastPointer)); break;
            case Key.Space:
                _spaceHeld = true;
                Cursor = _handCursor;
                break;
            case var _ when arrow: Nudge(e.Key, far: shift); break;
            default: e.Handled = false; break;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld) StopSpacePan();
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down) FinishNudge();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _lastPointer = e.GetPosition(Viewport);
    }

    /// <summary>Called when the window loses focus: drops held keys, so Space-panning doesn't get stuck.</summary>
    public void OnHostDeactivated()
    {
        StopSpacePan();
        FinishNudge();
    }

    private void StopSpacePan()
    {
        _spaceHeld = false;
        Cursor = null;
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
            SetView(_zoom, _panX - dx * step, _panY - dy * step);
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

    // Android's back button: closes whatever is open, one thing at a time, before leaving the app.
    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (DialogLayer.IsVisible) CloseDialog(null);
        else if (_board.Items.Any(i => i.IsEditing)) EndEditing();
        else if (CompactSearch.IsVisible) CloseSearch();
        else if (_board.HasSelection) _board.ClearSelection();
        else
        {
            SaveSession();
            return;
        }
        e.Handled = true;
    }

    // ---------- Message boxes ----------

    /// <summary>Shows a message with buttons (OK if none given) and returns the one picked, or null if dismissed.</summary>
    public Task<string?> ShowMessageAsync(string message, params string[] buttons)
    {
        CloseDialog(null);
        if (buttons.Length == 0) buttons = ["OK"];
        DialogText.Text = message;
        DialogButtons.Children.Clear();
        for (var i = 0; i < buttons.Length; i++)
        {
            var label = buttons[i];
            var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0) };
            button.Classes.Add(i == 0 ? "primary" : "secondary");
            button.Click += (_, _) => CloseDialog(label);
            DialogButtons.Children.Add(button);
        }
        DialogLayer.IsVisible = true;
        _dialog = new TaskCompletionSource<string?>();
        return _dialog.Task;
    }

    private void CloseDialog(string? answer)
    {
        if (_dialog is not { } dialog) return;
        _dialog = null;
        DialogLayer.IsVisible = false;
        Viewport.Focus();
        dialog.TrySetResult(answer);
    }

    // ---------- Helpers ----------

    private static bool IsPictureFile(string name) =>
        PictureExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());
}
