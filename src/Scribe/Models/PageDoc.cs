using System.Text.Json.Serialization;

namespace Scribe.Models;

/// <summary>
/// On-disk shape of a single page. This is the format contract: it is plain
/// JSON, documented, and deliberately free of anything you would need Scribe
/// itself to interpret. See docs/FORMAT.md.
/// </summary>
public sealed class PageDoc
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = CurrentSchema;

    public const string CurrentSchema = "scribe.page/1";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Untitled page";

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("modified")]
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;

    /// <summary>Page rule/grid backdrop.</summary>
    [JsonPropertyName("background")]
    public BackgroundDto Background { get; set; } = new();

    [JsonPropertyName("strokes")]
    public List<StrokeDto> Strokes { get; set; } = new();

    [JsonPropertyName("textBoxes")]
    public List<TextBoxDto> TextBoxes { get; set; } = new();

    [JsonPropertyName("images")]
    public List<ImageDto> Images { get; set; } = new();

    [JsonPropertyName("latexBlocks")]
    public List<LatexDto> LatexBlocks { get; set; } = new();

    [JsonPropertyName("tables")]
    public List<TableDto> Tables { get; set; } = new();

    /// <summary>
    /// Provenance, populated by the OneNote importer so a page can be traced
    /// back to (or re-synced from) its original.
    /// </summary>
    [JsonPropertyName("source")]
    public SourceDto? Source { get; set; }
}

public sealed class BackgroundDto
{
    /// <summary>One of: blank, ruled, grid, dots.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "ruled";

    [JsonPropertyName("spacing")]
    public double Spacing { get; set; } = 32;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#E8EEF5";
}

/// <summary>
/// A single ink stroke. Points are stored as a flat array with stride 3:
/// [x0, y0, pressure0, x1, y1, pressure1, ...]. Pressure is normalised 0..1.
/// Flat storage roughly halves file size versus nested arrays on real
/// handwriting, which runs to tens of thousands of points per page.
/// </summary>
public sealed class StrokeDto
{
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#1A1A1A";

    [JsonPropertyName("width")]
    public double Width { get; set; } = 2.5;

    [JsonPropertyName("height")]
    public double Height { get; set; } = 2.5;

    /// <summary>"ellipse" or "rectangle".</summary>
    [JsonPropertyName("tip")]
    public string Tip { get; set; } = "ellipse";

    [JsonPropertyName("highlighter")]
    public bool Highlighter { get; set; }

    /// <summary>Whether to Bezier-smooth the polyline when rendering.</summary>
    [JsonPropertyName("fit")]
    public bool FitToCurve { get; set; } = true;

    /// <summary>Whether stroke width responds to stylus pressure.</summary>
    [JsonPropertyName("pressureSensitive")]
    public bool PressureSensitive { get; set; } = true;

    [JsonPropertyName("points")]
    public double[] Points { get; set; } = Array.Empty<double>();
}

public sealed class TextBoxDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; } = 320;

    /// <summary>Null means "size to content", matching OneNote's behaviour.</summary>
    [JsonPropertyName("height")]
    public double? Height { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("fontSize")]
    public double FontSize { get; set; } = 15;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#1A1A1A";

    [JsonPropertyName("bold")]
    public bool Bold { get; set; }

    [JsonPropertyName("italic")]
    public bool Italic { get; set; }

    [JsonPropertyName("underline")]
    public bool Underline { get; set; }

    [JsonPropertyName("strikethrough")]
    public bool Strikethrough { get; set; }

    [JsonPropertyName("heading")]
    public string? Heading { get; set; }

    /// <summary>Optional FlowDocument XAML for rich text formatting.</summary>
    [JsonPropertyName("xaml")]
    public string? Xaml { get; set; }
}

public sealed class LatexDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("latex")]
    public string Latex { get; set; } = "";

    [JsonPropertyName("scale")]
    public double Scale { get; set; } = 20;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#1A1A1A";
}

public sealed class TableDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("rows")]
    public List<List<string>> Rows { get; set; } = new();

    [JsonPropertyName("columnWidths")]
    public List<double>? ColumnWidths { get; set; }

    [JsonPropertyName("hasHeader")]
    public bool HasHeader { get; set; } = true;
}

public sealed class ImageDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    /// <summary>Path relative to the section folder, e.g. "assets/ab12.png".</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";
}

public sealed class SourceDto
{
    [JsonPropertyName("app")]
    public string App { get; set; } = "onenote";

    /// <summary>The OneNote object ID, so a re-import can match this page.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("importedAt")]
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>notebook.json — ordering and identity for a notebook folder.</summary>
public sealed class NotebookDoc
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "scribe.notebook/1";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Notebook";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#7C5CE6";

    /// <summary>Section folder names, in display order.</summary>
    [JsonPropertyName("sectionOrder")]
    public List<string> SectionOrder { get; set; } = new();

    [JsonPropertyName("source")]
    public SourceDto? Source { get; set; }
}

/// <summary>section.json — ordering and identity for a section folder.</summary>
public sealed class SectionDoc
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = "scribe.section/1";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "Section";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#4C9AFF";

    /// <summary>Page file names (without .json), in display order.</summary>
    [JsonPropertyName("pageOrder")]
    public List<string> PageOrder { get; set; } = new();

    [JsonPropertyName("source")]
    public SourceDto? Source { get; set; }
}
