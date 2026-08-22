using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Scribe.Ink;

public enum ToolKind
{
    Pen,
    Highlighter,
    /// <summary>Removes whole strokes on contact.</summary>
    StrokeEraser,
    /// <summary>Rubs away only the part of a stroke under the tip.</summary>
    PointEraser,
    /// <summary>Lasso selection of ink and objects.</summary>
    Select,
    /// <summary>Click anywhere to place a text container.</summary>
    Text,
    /// <summary>Drag to move the page.</summary>
    Pan,
}

/// <summary>
/// One entry in the pen tray. Mirrors OneNote's model, where a pen is a saved
/// combination of colour and thickness rather than two separate settings.
/// </summary>
public sealed class InkTool : INotifyPropertyChanged
{
    private ToolKind _kind = ToolKind.Pen;
    private Color _color = Color.FromRgb(0x1A, 0x1A, 0x1A);
    private double _width = 2.4;
    private bool _pressureSensitive = true;
    private string _name = "Pen";

    public ToolKind Kind
    {
        get => _kind;
        set => Set(ref _kind, value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public Color Color
    {
        get => _color;
        set
        {
            if (Set(ref _color, value)) OnPropertyChanged(nameof(Brush));
        }
    }

    /// <summary>Nib width in DIPs at full pressure.</summary>
    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public bool PressureSensitive
    {
        get => _pressureSensitive;
        set => Set(ref _pressureSensitive, value);
    }

    public Brush Brush => new SolidColorBrush(Color);

    public bool IsEraser => Kind is ToolKind.StrokeEraser or ToolKind.PointEraser;

    public bool IsInkTool => Kind is ToolKind.Pen or ToolKind.Highlighter;

    public InkTool Clone() => new()
    {
        Kind = Kind,
        Name = Name,
        Color = Color,
        Width = Width,
        PressureSensitive = PressureSensitive,
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    // The default tray. Colours are picked to stay legible on the ruled
    // background and to remain distinguishable for the common forms of colour
    // blindness (no red/green-only pairing).
    public static IReadOnlyList<InkTool> DefaultTray() => new List<InkTool>
    {
        new() { Name = "Ink black",  Kind = ToolKind.Pen, Color = Color.FromRgb(0x1A, 0x1A, 0x1A), Width = 2.4 },
        new() { Name = "Blue",       Kind = ToolKind.Pen, Color = Color.FromRgb(0x1D, 0x4E, 0xD8), Width = 2.4 },
        new() { Name = "Red",        Kind = ToolKind.Pen, Color = Color.FromRgb(0xC0, 0x28, 0x28), Width = 2.4 },
        new() { Name = "Green",      Kind = ToolKind.Pen, Color = Color.FromRgb(0x15, 0x7F, 0x3C), Width = 2.4 },
        new() { Name = "Fine black", Kind = ToolKind.Pen, Color = Color.FromRgb(0x1A, 0x1A, 0x1A), Width = 1.2 },
        new() { Name = "Yellow highlighter", Kind = ToolKind.Highlighter, Color = Color.FromRgb(0xFF, 0xE0, 0x3D), Width = 16, PressureSensitive = false },
        new() { Name = "Green highlighter",  Kind = ToolKind.Highlighter, Color = Color.FromRgb(0x8C, 0xE9, 0x9B), Width = 16, PressureSensitive = false },
    };
}
