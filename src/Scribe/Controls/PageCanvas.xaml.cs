using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scribe.Ink;
using Scribe.Models;
using Scribe.Services;
using Scribe.Storage;

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

    public PageCanvas()
    {
        InitializeComponent();

        Ink.Strokes.StrokesChanged += OnStrokesChanged;
        Ink.SelectionMoved += (_, _) => MarkDirty();
        Ink.SelectionResized += (_, _) => MarkDirty();

        SetPageSize(DefaultPageWidth, DefaultPageHeight);
        ApplyTool();
    }

    public ScribeInkCanvas InkSurface => Ink;

    public string? PageFile => _pageFile;

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

    private void ApplyTool()
    {
        Ink.ActiveTool = _activeTool;

        // Text containers must not swallow pen input while an ink tool is
        // active, or writing across one would land in the textbox instead of
        // on the page.
        bool textInteractive = _activeTool.Kind is ToolKind.Text or ToolKind.Select;
        foreach (var child in Ink.Children.OfType<PageTextBox>())
        {
            child.IsHitTestVisible = textInteractive;
            child.Focusable = textInteractive;
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

            foreach (var stroke in StrokeCodec.FromDtos(doc.Strokes))
                Ink.Strokes.Add(stroke);

            foreach (var tb in doc.TextBoxes) AddTextBoxControl(tb);
            foreach (var img in doc.Images) AddImageControl(img);

            Backdrop.Kind = doc.Background.Kind;
            Backdrop.Spacing = doc.Background.Spacing;

            // The colour stored in the page describes the light-mode paper. On
            // a dark page it would glare, so the theme supplies the rule colour
            // instead and the stored value is left untouched on disk.
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

            // Empty containers are noise; drop them rather than persist them.
            if (!string.IsNullOrWhiteSpace(tb.Model.Text))
                _doc.TextBoxes.Add(tb.Model);
        }

        _doc.Images.Clear();
        foreach (var img in Ink.Children.OfType<Image>())
        {
            if (img.Tag is not ImageDto dto) continue;
            dto.X = InkCanvas.GetLeft(img);
            dto.Y = InkCanvas.GetTop(img);
            dto.Width = img.Width;
            dto.Height = img.Height;
            _doc.Images.Add(dto);
        }

        return _doc;
    }

    public void Unload()
    {
        _pageFile = null;
        _sectionDir = null;
        _doc = new PageDoc();

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
            // Copy the change sets: the event args are reused by WPF and must
            // not be captured by reference in a closure that outlives the call.
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

    private PageTextBox AddTextBoxControl(TextBoxDto dto)
    {
        var tb = new PageTextBox(dto);
        InkCanvas.SetLeft(tb, dto.X);
        InkCanvas.SetTop(tb, dto.Y);

        tb.TextChanged += (_, _) => MarkDirty();
        tb.LostKeyboardFocus += (_, _) => RemoveIfEmpty(tb);

        Ink.Children.Add(tb);
        return tb;
    }

    private void RemoveIfEmpty(PageTextBox tb)
    {
        if (!string.IsNullOrWhiteSpace(tb.Text)) return;

        Ink.Children.Remove(tb);
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

        // Layout has not run yet, so focus on the next dispatcher pass.
        Dispatcher.BeginInvoke(new Action(() => tb.Focus()),
            System.Windows.Threading.DispatcherPriority.Input);

        MarkDirty();
    }

    private void AddImageControl(ImageDto dto)
    {
        if (_sectionDir is null) return;

        var path = Path.Combine(_sectionDir, dto.File.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);

            // Load the bytes up front so the file is not held open, which would
            // otherwise block the folder from syncing or being moved.
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var image = new Image
            {
                Source = bitmap,
                Width = dto.Width > 0 ? dto.Width : bitmap.PixelWidth,
                Height = dto.Height > 0 ? dto.Height : bitmap.PixelHeight,
                Stretch = Stretch.Fill,
                Tag = dto,
            };

            InkCanvas.SetLeft(image, dto.X);
            InkCanvas.SetTop(image, dto.Y);
            Ink.Children.Add(image);
        }
        catch
        {
            // A missing or unreadable asset should cost one image, not the page.
        }
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

    /// <summary>
    /// Extends the page when writing approaches an edge, so the surface behaves
    /// like OneNote's endless page instead of a fixed sheet.
    /// </summary>
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

        // Only ever grow within a session. Shrinking mid-edit would yank the
        // page out from under the scroll position while erasing.
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

        // Keep whatever sits under the pointer pinned there across the zoom.
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

    // Middle-drag panning, which is the mouse equivalent of a two-finger drag.
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

            // Clicking an existing container should edit it, not stack a new
            // one on top.
            if (e.OriginalSource is not PageTextBox && !IsInsideTextBox(e.OriginalSource))
            {
                CreateTextBoxAt(pt);
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewMouseDown(e);
    }

    private static bool IsInsideTextBox(object? source)
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is PageTextBox) return true;
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
    //
    // Touch is intercepted here, above the ink canvas, so a palm or a stray
    // finger drives the viewport instead of leaving a mark. One finger pans,
    // two fingers pan and pinch-zoom together.

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

        // Mean distance from the centroid, which generalises pinch to any
        // number of contacts instead of only handling exactly two.
        double spread = 0;
        foreach (var p in _touches.Values)
            spread += (p - centre).Length;
        spread /= _touches.Count;

        return (_touches.Count, centre, spread);
    }
}
