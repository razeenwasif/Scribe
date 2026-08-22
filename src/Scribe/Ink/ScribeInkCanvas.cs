using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;

namespace Scribe.Ink;

/// <summary>How finger touch behaves while an ink tool is active.</summary>
public enum TouchMode
{
    /// <summary>Touch pans and zooms the page; only the pen draws.</summary>
    Gesture,

    /// <summary>Touch draws too, for machines with no pen.</summary>
    Draw,
}

/// <summary>
/// The drawing surface.
///
/// Two things here matter more than anything else for handwriting:
/// wet ink goes through <see cref="ScribeDynamicRenderer"/> on the pen thread,
/// and touch is separated from pen input so a resting palm cannot leave marks.
/// </summary>
public sealed class ScribeInkCanvas : InkCanvas
{
    private readonly ScribeDynamicRenderer _renderer = new();

    public ScribeInkCanvas()
    {
        DynamicRenderer = _renderer;

        // The pen's eraser end should erase regardless of the selected tool;
        // that is muscle memory from every real pen and from OneNote itself.
        EditingModeInverted = InkCanvasEditingMode.EraseByStroke;

        Background = System.Windows.Media.Brushes.Transparent;

        // Suppresses the press-and-hold right-click ring, which otherwise
        // interrupts slow, deliberate handwriting.
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);
    }

    public TouchMode TouchMode { get; set; } = TouchMode.Gesture;

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

    /// <summary>Pushes the current tool onto the canvas's editing state.</summary>
    public void ApplyTool()
    {
        var tool = _activeTool;

        var attributes = new DrawingAttributes
        {
            Color = tool.Color,
            Width = tool.Width,
            Height = tool.Width,
            StylusTip = StylusTip.Ellipse,
            IsHighlighter = tool.Kind == ToolKind.Highlighter,

            // Our own resampling does the smoothing; leaving WPF's Bezier fit on
            // as well would round the geometry twice and soften tight letters.
            FitToCurve = false,
        };

        DefaultDrawingAttributes = attributes;
        _renderer.DrawingAttributes = attributes;
        _renderer.PressureSensitive = tool.PressureSensitive;

        EditingMode = tool.Kind switch
        {
            ToolKind.Pen or ToolKind.Highlighter => InkCanvasEditingMode.Ink,
            ToolKind.StrokeEraser => InkCanvasEditingMode.EraseByStroke,
            ToolKind.PointEraser => InkCanvasEditingMode.EraseByPoint,
            ToolKind.Select => InkCanvasEditingMode.Select,
            _ => InkCanvasEditingMode.None,
        };

        if (tool.Kind == ToolKind.PointEraser)
        {
            double size = Math.Max(tool.Width, 4);
            EraserShape = new EllipseStylusShape(size, size);
        }

        UseCustomCursor = false;
    }

    /// <summary>
    /// Swaps WPF's plain stroke for one that renders with pressure. The stroke
    /// has already been built by the time this fires, so it is rebuilt in place
    /// rather than intercepted earlier.
    /// </summary>
    protected override void OnStrokeCollected(InkCanvasStrokeCollectedEventArgs e)
    {
        if (e.Stroke is ScribeStroke)
        {
            base.OnStrokeCollected(e);
            return;
        }

        var replacement = new ScribeStroke(
            e.Stroke.StylusPoints.Clone(),
            e.Stroke.DrawingAttributes.Clone())
        {
            PressureSensitive = _activeTool.PressureSensitive,
        };

        Strokes.Remove(e.Stroke);
        Strokes.Add(replacement);

        base.OnStrokeCollected(new InkCanvasStrokeCollectedEventArgs(replacement));
    }

    // ------------------------------------------------------------ palm rejection
    //
    // A resting palm registers as touch, never as pen, so declining to ink from
    // touch *is* palm rejection — and unlike heuristic contact-size filtering it
    // has no false positives and no tuning.
    //
    // The interception itself lives in PageCanvas: preview events tunnel from
    // the root down, so the page host sees each touch before this canvas does
    // and can route it to panning without this class ever being involved.
}
