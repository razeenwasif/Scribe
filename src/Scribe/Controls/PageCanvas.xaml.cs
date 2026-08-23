using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Scribe.Ink;
using Scribe.Models;
using Scribe.Services;
using Scribe.Storage;
using Scribe.Views;

namespace Scribe.Controls;

public partial class PageCanvas : UserControl
{
    private const double DefaultPageWidth = 1180;
    private const double DefaultPageHeight = 1620;

    /// <summary>Blank space kept below and right of the furthest content.</summary>
    private const double GrowthMargin = 600;

    private const double MinZoom = 0.15;
    private const double MaxZoom = 6.0;

    private PageDoc _doc = new();
    private string? _pageFile;
    private string? _sectionDir;

    /// <summary>Suppresses dirty/undo recording while a page is being populated.</summary>
    private bool _loading;

    public UndoService Undo { get; } = new();

    /// <summary>Raised whenever the page content changes and needs saving.</summary>
    public event EventHandler? ContentChanged;

    /// <summary>Raised when the active text box selection or focus changes.</summary>
    public event EventHandler? ActiveTextBoxSelectionChanged;

    public PageTextBox? ActiveTextBox { get; private set; }

    public PageCanvas()
    {
        InitializeComponent();

        Ink.Strokes.StrokesChanged += OnStrokesChanged;
        Ink.SelectionMoved += (_, _) => MarkDirty();
        Ink.SelectionResized += (_, _) => MarkDirty();

        SetPageSize(DefaultPageWidth, DefaultPageHeight);
        ApplyTool();

        // Command bindings for copy/paste
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, _) => PasteFromClipboard()));
    }

    public ScribeInkCanvas InkSurface => Ink;

    public string? PageFile => _pageFile;
    public string? SectionDir => _sectionDir;

    // ------------------------------------------------------------------- tooling

    private InkTool _activeTool = new();

    public InkTool ActiveTool
    {
        get => _activeTool;
        set
        {
            _activeTool = value;
            ApplyTool();
        }
    }

    public void ApplyTool()
    {
        Ink.ActiveTool = _activeTool;

        // Elements must not swallow pen input while an ink tool is active
        bool interactive = _activeTool.Kind is ToolKind.Text or ToolKind.Select;
        foreach (UIElement child in Ink.Children)
        {
            child.IsHitTestVisible = interactive;
            child.Focusable = interactive;
        }

        Cursor = _activeTool.Kind switch
        {
            ToolKind.Text => Cursors.IBeam,
            ToolKind.Pan => Cursors.SizeAll,
            _ => Cursors.Arrow,
        };
    }

    // ------------------------------------------------------------- load and save

    public void LoadPage(string sectionDir, string pageFile, PageDoc doc)
    {
        _loading = true;
        try
        {
            _sectionDir = sectionDir;
            _pageFile = pageFile;
            _doc = doc;

            Ink.Strokes.Clear();
            Ink.Children.Clear();
            ActiveTextBox = null;

            foreach (var stroke in StrokeCodec.FromDtos(doc.Strokes))
                Ink.Strokes.Add(stroke);

            foreach (var tb in doc.TextBoxes) AddTextBoxControl(tb);
            foreach (var img in doc.Images) AddImageControl(img);
            foreach (var lx in doc.LatexBlocks) AddLatexControl(lx);
            foreach (var tbl in doc.Tables) AddTableControl(tbl);

            Backdrop.Kind = doc.Background.Kind;
            Backdrop.Spacing = doc.Background.Spacing;

            Backdrop.LineBrush = InkTheme.IsDark
                ? (Brush)FindResource("PageLineBrush")
                : new SolidColorBrush(
                    StrokeCodec.FromHex(doc.Background.Color, Color.FromRgb(0xE8, 0xEE, 0xF5)));

            Undo.Clear();
            GrowToFitContent();
            Scroller.ScrollToTop();
            Scroller.ScrollToLeftEnd();
        }
        finally
        {
            _loading = false;
        }

        ApplyTool();
    }

    /// <summary>Flushes live control state into the page document.</summary>
    public PageDoc ToDoc()
    {
        _doc.Strokes = StrokeCodec.ToDtos(Ink.Strokes);

        _doc.TextBoxes.Clear();
        foreach (var tb in Ink.Children.OfType<PageTextBox>())
        {
            tb.SyncToModel();
            if (!string.IsNullOrWhiteSpace(tb.Model.Text))
                _doc.TextBoxes.Add(tb.Model);
        }

        _doc.Images.Clear();
        foreach (var img in Ink.Children.OfType<PageImageControl>())
        {
            img.SyncToModel();
            _doc.Images.Add(img.Model);
        }

        _doc.LatexBlocks.Clear();
        foreach (var lx in Ink.Children.OfType<PageLatexControl>())
        {
            lx.SyncToModel();
            if (!string.IsNullOrWhiteSpace(lx.Model.Latex))
                _doc.LatexBlocks.Add(lx.Model);
        }

        _doc.Tables.Clear();
        foreach (var tbl in Ink.Children.OfType<PageTableControl>())
        {
            tbl.SyncToModel();
            if (tbl.Model.Rows.Count > 0)
                _doc.Tables.Add(tbl.Model);
        }

        return _doc;
    }

    public void Unload()
    {
        _pageFile = null;
        _sectionDir = null;
        _doc = new PageDoc();
        ActiveTextBox = null;

        _loading = true;
        try
        {
            Ink.Strokes.Clear();
            Ink.Children.Clear();
        }
        finally
        {
            _loading = false;
        }

        Undo.Clear();
    }

    private void MarkDirty()
    {
        if (_loading) return;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    // -------------------------------------------------------------------- undo

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_loading) return;

        if (!Undo.IsApplying)
        {
            var added = e.Added.ToList();
            var removed = e.Removed.ToList();

            string label = added.Count > 0 && removed.Count == 0 ? "Write"
                         : removed.Count > 0 && added.Count == 0 ? "Erase"
                         : "Edit ink";

            Undo.Record(label,
                undo: () =>
                {
                    foreach (var s in added) Ink.Strokes.Remove(s);
                    foreach (var s in removed) Ink.Strokes.Add(s);
                },
                redo: () =>
                {
                    foreach (var s in removed) Ink.Strokes.Remove(s);
                    foreach (var s in added) Ink.Strokes.Add(s);
                });
        }

        GrowToFitContent();
        MarkDirty();
    }

    // --------------------------------------------------------------- text boxes

    public PageTextBox AddTextBoxControl(TextBoxDto dto)
    {
        var tb = new PageTextBox(dto);
        InkCanvas.SetLeft(tb, dto.X);
        InkCanvas.SetTop(tb, dto.Y);

        tb.ContentChanged += (_, _) =>
        {
            GrowToFitContent();
            MarkDirty();
        };

        tb.SelectionStateChanged += (_, _) =>
        {
            ActiveTextBox = tb;
            ActiveTextBoxSelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        tb.Editor.GotKeyboardFocus += (_, _) =>
        {
            ActiveTextBox = tb;
            ActiveTextBoxSelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        tb.Editor.LostKeyboardFocus += (_, _) =>
        {
            RemoveIfEmpty(tb);
            ActiveTextBoxSelectionChanged?.Invoke(this, EventArgs.Empty);
        };

        tb.RequestDelete += (_, _) =>
        {
            RemoveChildWithUndo(tb, "Delete text");
        };

        Ink.Children.Add(tb);
        return tb;
    }

    private void RemoveIfEmpty(PageTextBox tb)
    {
        if (!string.IsNullOrWhiteSpace(tb.Text)) return;

        Ink.Children.Remove(tb);
        if (ActiveTextBox == tb) ActiveTextBox = null;
        MarkDirty();
    }

    /// <summary>Places a new text container and puts the caret in it.</summary>
    public void CreateTextBoxAt(Point pagePoint)
    {
        var dto = new TextBoxDto
        {
            X = Math.Max(0, pagePoint.X),
            Y = Math.Max(0, pagePoint.Y),
            Width = 360,
        };

        var tb = AddTextBoxControl(dto);
        tb.IsHitTestVisible = true;
        tb.Focusable = true;

        if (!Undo.IsApplying)
        {
            Undo.Record("Add text",
                undo: () => Ink.Children.Remove(tb),
                redo: () => Ink.Children.Add(tb));
        }

        Dispatcher.BeginInvoke(new Action(() => tb.Editor.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);

        MarkDirty();
    }

    // ------------------------------------------------------------------- images

    public PageImageControl? AddImageControl(ImageDto dto)
    {
        if (_sectionDir is null) return null;

        var imgControl = new PageImageControl(dto, _sectionDir);
        InkCanvas.SetLeft(imgControl, dto.X);
        InkCanvas.SetTop(imgControl, dto.Y);

        imgControl.Changed += (_, _) =>
        {
            GrowToFitContent();
            MarkDirty();
        };

        imgControl.RequestDelete += (_, _) =>
        {
            RemoveChildWithUndo(imgControl, "Delete image");
        };

        Ink.Children.Add(imgControl);
        return imgControl;
    }

    // ------------------------------------------------------------- LaTeX blocks

    public PageLatexControl AddLatexControl(LatexDto dto)
    {
        var lxControl = new PageLatexControl(dto);
        InkCanvas.SetLeft(lxControl, dto.X);
        InkCanvas.SetTop(lxControl, dto.Y);

        lxControl.Changed += (_, _) =>
        {
            GrowToFitContent();
            MarkDirty();
        };

        lxControl.RequestDelete += (_, _) =>
        {
            RemoveChildWithUndo(lxControl, "Delete equation");
        };

        Ink.Children.Add(lxControl);
        return lxControl;
    }

    public void InsertLatexDialog(Point? atPoint = null)
    {
        var owner = Window.GetWindow(this);
        var dialog = new LatexEditDialog("", 22, isEditing: false);
        if (owner is not null) dialog.Owner = owner;

        if (dialog.ShowDialog() == true)
        {
            var pos = atPoint ?? GetViewportCenter();
            var dto = new LatexDto
            {
                X = pos.X,
                Y = pos.Y,
                Latex = dialog.ResultLatex,
                Scale = dialog.ResultScale,
            };

            var lx = AddLatexControl(dto);
            if (!Undo.IsApplying)
            {
                Undo.Record("Insert equation",
                    undo: () => Ink.Children.Remove(lx),
                    redo: () => Ink.Children.Add(lx));
            }

            GrowToFitContent();
            MarkDirty();
        }
    }

    // ------------------------------------------------------------------- tables

    public PageTableControl AddTableControl(TableDto dto)
    {
        var tblControl = new PageTableControl(dto);
        InkCanvas.SetLeft(tblControl, dto.X);
        InkCanvas.SetTop(tblControl, dto.Y);

        tblControl.Changed += (_, _) =>
        {
            GrowToFitContent();
            MarkDirty();
        };

        tblControl.RequestDelete += (_, _) =>
        {
            RemoveChildWithUndo(tblControl, "Delete table");
        };

        Ink.Children.Add(tblControl);
        return tblControl;
    }

    public void InsertTableDialog(Point? atPoint = null)
    {
        var owner = Window.GetWindow(this);
        var dialog = new TableInsertDialog();
        if (owner is not null) dialog.Owner = owner;

        if (dialog.ShowDialog() == true)
        {
            var pos = atPoint ?? GetViewportCenter();
            var rows = new List<List<string>>();

            // Header row
            var header = new List<string>();
            for (int c = 1; c <= dialog.Columns; c++)
                header.Add(dialog.HasHeader ? $"Header {c}" : "");
            rows.Add(header);

            for (int r = 1; r < dialog.Rows; r++)
            {
                var row = new List<string>();
                for (int c = 1; c <= dialog.Columns; c++) row.Add("");
                rows.Add(row);
            }

            var dto = new TableDto
            {
                X = pos.X,
                Y = pos.Y,
                Rows = rows,
                HasHeader = dialog.HasHeader,
            };

            var tbl = AddTableControl(dto);
            if (!Undo.IsApplying)
            {
                Undo.Record("Insert table",
                    undo: () => Ink.Children.Remove(tbl),
                    redo: () => Ink.Children.Add(tbl));
            }

            GrowToFitContent();
            MarkDirty();
        }
    }

    // ------------------------------------------------------------- clipboard & paste

    public void PasteFromClipboard(Point? atPoint = null)
    {
        if (_sectionDir is null) return;

        var pos = atPoint ?? GetViewportCenter();

        // 1. Check for Image in clipboard
        if (Clipboard.ContainsImage())
        {
            var imgSource = Clipboard.GetImage();
            if (imgSource is not null)
            {
                PasteBitmapSource(imgSource, pos);
                return;
            }
        }

        // 2. Check for File Drop list with images
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();
            foreach (string? file in files)
            {
                if (!string.IsNullOrEmpty(file) && IsImageExtension(Path.GetExtension(file)))
                {
                    PasteImageFile(file, pos);
                    return;
                }
            }
        }

        // 3. Check for Table in clipboard
        var tableDto = PageTableControl.TryParseClipboardTable();
        if (tableDto is not null && tableDto.Rows.Count > 0)
        {
            tableDto.X = pos.X;
            tableDto.Y = pos.Y;
            var tbl = AddTableControl(tableDto);

            if (!Undo.IsApplying)
            {
                Undo.Record("Paste table",
                    undo: () => Ink.Children.Remove(tbl),
                    redo: () => Ink.Children.Add(tbl));
            }

            GrowToFitContent();
            MarkDirty();
            return;
        }

        // 4. Plain / Rich Text
        if (Clipboard.ContainsText())
        {
            if (ActiveTextBox is not null && ActiveTextBox.Editor.IsKeyboardFocused)
            {
                // Let the focused RichTextBox handle its native paste
                ActiveTextBox.Editor.Paste();
            }
            else
            {
                var text = Clipboard.GetText();
                var dto = new TextBoxDto
                {
                    X = pos.X,
                    Y = pos.Y,
                    Text = text,
                    Width = 400,
                };
                var tb = AddTextBoxControl(dto);
                if (!Undo.IsApplying)
                {
                    Undo.Record("Paste text",
                        undo: () => Ink.Children.Remove(tb),
                        redo: () => Ink.Children.Add(tb));
                }
                GrowToFitContent();
                MarkDirty();
            }
        }
    }

    public void InsertImageFromFile()
    {
        if (_sectionDir is null) return;

        var dlg = new OpenFileDialog
        {
            Title = "Insert Image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All Files (*.*)|*.*",
        };

        if (dlg.ShowDialog() == true)
        {
            PasteImageFile(dlg.FileName, GetViewportCenter());
        }
    }

    private void PasteImageFile(string filePath, Point pos)
    {
        if (_sectionDir is null || !File.Exists(filePath)) return;

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";

            var relativePath = SaveAssetBytes(bytes, ext);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Path.Combine(_sectionDir, relativePath));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            double width = bitmap.PixelWidth > 700 ? 700 : bitmap.PixelWidth;
            double height = bitmap.PixelWidth > 700 ? (bitmap.PixelHeight * 700.0 / bitmap.PixelWidth) : bitmap.PixelHeight;

            var dto = new ImageDto
            {
                X = pos.X,
                Y = pos.Y,
                Width = width,
                Height = height,
                File = relativePath,
            };

            var img = AddImageControl(dto);
            if (img is not null && !Undo.IsApplying)
            {
                Undo.Record("Insert image",
                    undo: () => Ink.Children.Remove(img),
                    redo: () => Ink.Children.Add(img));
            }

            GrowToFitContent();
            MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not insert image: {ex.Message}", "Image Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PasteBitmapSource(BitmapSource bitmapSource, Point pos)
    {
        if (_sectionDir is null) return;

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            var bytes = ms.ToArray();

            var relativePath = SaveAssetBytes(bytes, ".png");

            double width = bitmapSource.PixelWidth > 700 ? 700 : bitmapSource.PixelWidth;
            double height = bitmapSource.PixelWidth > 700 ? (bitmapSource.PixelHeight * 700.0 / bitmapSource.PixelWidth) : bitmapSource.PixelHeight;

            var dto = new ImageDto
            {
                X = pos.X,
                Y = pos.Y,
                Width = width,
                Height = height,
                File = relativePath,
            };

            var img = AddImageControl(dto);
            if (img is not null && !Undo.IsApplying)
            {
                Undo.Record("Paste image",
                    undo: () => Ink.Children.Remove(img),
                    redo: () => Ink.Children.Add(img));
            }

            GrowToFitContent();
            MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not paste image: {ex.Message}", "Paste Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string SaveAssetBytes(byte[] data, string extension)
    {
        if (_sectionDir is null) throw new InvalidOperationException("No section open.");
        var assets = Path.Combine(_sectionDir, "assets");
        Directory.CreateDirectory(assets);

        var name = Guid.NewGuid().ToString("n")[..12] + extension;
        File.WriteAllBytes(Path.Combine(assets, name), data);
        return Path.Combine("assets", name).Replace('\\', '/');
    }

    private static bool IsImageExtension(string? ext)
    {
        if (string.IsNullOrEmpty(ext)) return false;
        ext = ext.ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";
    }

    private Point GetViewportCenter()
    {
        double scale = ZoomTransform.ScaleX;
        if (scale <= 0) scale = 1.0;

        double vx = (Scroller.HorizontalOffset + Scroller.ViewportWidth / 2) / scale;
        double vy = (Scroller.VerticalOffset + Scroller.ViewportHeight / 2) / scale;

        return new Point(Math.Max(40, vx - 100), Math.Max(40, vy - 100));
    }

    private void RemoveChildWithUndo(UIElement element, string actionLabel)
    {
        if (!Ink.Children.Contains(element)) return;

        Ink.Children.Remove(element);
        if (ReferenceEquals(ActiveTextBox, element)) ActiveTextBox = null;

        if (!Undo.IsApplying)
        {
            Undo.Record(actionLabel,
                undo: () => Ink.Children.Add(element),
                redo: () => Ink.Children.Remove(element));
        }

        MarkDirty();
    }

    // ------------------------------------------------------------ page geometry

    private void SetPageSize(double width, double height)
    {
        PageSurface.Width = width;
        PageSurface.Height = height;
        Ink.Width = width;
        Ink.Height = height;
        Backdrop.Width = width;
        Backdrop.Height = height;
    }

    private void GrowToFitContent()
    {
        double maxX = 0, maxY = 0;

        if (Ink.Strokes.Count > 0)
        {
            var b = Ink.Strokes.GetBounds();
            if (!b.IsEmpty)
            {
                maxX = Math.Max(maxX, b.Right);
                maxY = Math.Max(maxY, b.Bottom);
            }
        }

        foreach (UIElement child in Ink.Children)
        {
            double left = InkCanvas.GetLeft(child);
            double top = InkCanvas.GetTop(child);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            maxX = Math.Max(maxX, left + child.RenderSize.Width);
            maxY = Math.Max(maxY, top + child.RenderSize.Height);
        }

        double width = Math.Max(DefaultPageWidth, maxX + GrowthMargin);
        double height = Math.Max(DefaultPageHeight, maxY + GrowthMargin);

        width = Math.Max(width, PageSurface.Width);
        height = Math.Max(height, PageSurface.Height);

        if (Math.Abs(width - PageSurface.Width) > 0.5 ||
            Math.Abs(height - PageSurface.Height) > 0.5)
        {
            SetPageSize(width, height);
        }
    }

    // -------------------------------------------------------------- zoom and pan

    public double Zoom => ZoomTransform.ScaleX;

    public void SetZoom(double scale) =>
        ZoomAt(scale, new Point(Scroller.ViewportWidth / 2, Scroller.ViewportHeight / 2));

    private void ZoomAt(double scale, Point viewportPoint)
    {
        double oldScale = ZoomTransform.ScaleX;
        double newScale = Math.Clamp(scale, MinZoom, MaxZoom);
        if (Math.Abs(newScale - oldScale) < 0.0001) return;

        double contentX = (Scroller.HorizontalOffset + viewportPoint.X) / oldScale;
        double contentY = (Scroller.VerticalOffset + viewportPoint.Y) / oldScale;

        ZoomTransform.ScaleX = newScale;
        ZoomTransform.ScaleY = newScale;
        Scroller.UpdateLayout();

        Scroller.ScrollToHorizontalOffset(contentX * newScale - viewportPoint.X);
        Scroller.ScrollToVerticalOffset(contentY * newScale - viewportPoint.Y);

        ZoomLabel.Text = $"{newScale * 100:0}%";
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double factor = e.Delta > 0 ? 1.12 : 1 / 1.12;
            ZoomAt(ZoomTransform.ScaleX * factor, e.GetPosition(Scroller));
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseWheel(e);
    }

    private bool _mousePanning;
    private Point _mousePanOrigin;

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        bool wantsPan = e.ChangedButton == MouseButton.Middle
                        || (e.ChangedButton == MouseButton.Left && ActiveTool.Kind == ToolKind.Pan);

        if (wantsPan)
        {
            _mousePanning = true;
            _mousePanOrigin = e.GetPosition(Scroller);
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && ActiveTool.Kind == ToolKind.Text)
        {
            var pt = e.GetPosition(Ink);

            if (!IsInsideInteractiveElement(e.OriginalSource))
            {
                CreateTextBoxAt(pt);
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewMouseDown(e);
    }

    private static bool IsInsideInteractiveElement(object? source)
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is PageTextBox or PageImageControl or PageLatexControl or PageTableControl) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_mousePanning)
        {
            var now = e.GetPosition(Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - (now.X - _mousePanOrigin.X));
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset - (now.Y - _mousePanOrigin.Y));
            _mousePanOrigin = now;
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (_mousePanning)
        {
            _mousePanning = false;
            ReleaseMouseCapture();
            ApplyTool();
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseUp(e);
    }

    // ------------------------------------------------------------ touch gestures

    private readonly Dictionary<int, Point> _touches = new();

    private bool DivertTouch => Ink.TouchMode == TouchMode.Gesture;

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        if (!DivertTouch)
        {
            base.OnPreviewTouchDown(e);
            return;
        }

        _touches[e.TouchDevice.Id] = e.GetTouchPoint(Scroller).Position;
        e.TouchDevice.Capture(this);
        e.Handled = true;
    }

    protected override void OnPreviewTouchMove(TouchEventArgs e)
    {
        if (!DivertTouch || !_touches.ContainsKey(e.TouchDevice.Id))
        {
            base.OnPreviewTouchMove(e);
            return;
        }

        var before = Snapshot();
        _touches[e.TouchDevice.Id] = e.GetTouchPoint(Scroller).Position;
        var after = Snapshot();

        if (before.Count >= 2 && after.Spread > 1 && before.Spread > 1)
        {
            ZoomAt(ZoomTransform.ScaleX * (after.Spread / before.Spread), after.Centre);
        }

        Scroller.ScrollToHorizontalOffset(
            Scroller.HorizontalOffset - (after.Centre.X - before.Centre.X));
        Scroller.ScrollToVerticalOffset(
            Scroller.VerticalOffset - (after.Centre.Y - before.Centre.Y));

        e.Handled = true;
    }

    protected override void OnPreviewTouchUp(TouchEventArgs e)
    {
        if (_touches.Remove(e.TouchDevice.Id))
        {
            e.TouchDevice.Capture(null);
            e.Handled = true;
            return;
        }

        base.OnPreviewTouchUp(e);
    }

    private (int Count, Point Centre, double Spread) Snapshot()
    {
        if (_touches.Count == 0) return (0, default, 0);

        double sx = 0, sy = 0;
        foreach (var p in _touches.Values)
        {
            sx += p.X;
            sy += p.Y;
        }

        var centre = new Point(sx / _touches.Count, sy / _touches.Count);

        double spread = 0;
        foreach (var p in _touches.Values)
            spread += (p - centre).Length;
        spread /= _touches.Count;

        return (_touches.Count, centre, spread);
    }
}
