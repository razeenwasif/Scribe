using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Ink;
using System.Windows.Media;
using System.Xml.Linq;
using Scribe.Models;
using Scribe.Storage;

namespace Scribe.Import;

public sealed class ImportOptions
{
    /// <summary>Notebook IDs to import. Empty means everything.</summary>
    public HashSet<string> NotebookIds { get; init; } = new();

    /// <summary>Bring across the rendered images embedded in pages.</summary>
    public bool IncludeImages { get; init; } = true;
}

public sealed class ImportSummary
{
    public int Notebooks { get; set; }
    public int Sections { get; set; }
    public int Pages { get; set; }
    public int Strokes { get; set; }
    public int Images { get; set; }
    public List<string> Warnings { get; } = new();
}

public sealed record NotebookChoice(string Id, string Name);

/// <summary>
/// Copies OneNote content into the Scribe workspace.
///
/// Handwriting is the point of this importer. OneNote stores ink as base64 ISF
/// inside &lt;one:InkData&gt;, which is the same format WPF's own StrokeCollection
/// reads, so strokes arrive as real strokes with their pressure intact rather
/// than as flattened pictures of writing.
/// </summary>
public sealed class OneNoteImporter
{
    /// <summary>OneNote measures in points; WPF measures in 1/96 inch.</summary>
    private const double PointsToDip = 96.0 / 72.0;

    private readonly Workspace _workspace;

    public OneNoteImporter(Workspace workspace) => _workspace = workspace;

    public event Action<string>? Progress;

    private void Report(string message) => Progress?.Invoke(message);

    /// <summary>Lists the notebooks OneNote currently has open.</summary>
    public static List<NotebookChoice> ListNotebooks()
    {
        using var one = new OneNoteInterop();
        one.Connect();

        var xml = one.GetHierarchy("", OneNoteInterop.ScopeNotebooks);
        var doc = XDocument.Parse(xml);
        var ns = doc.Root!.Name.Namespace;

        return doc.Root.Elements(ns + "Notebook")
            .Select(n => new NotebookChoice(
                (string?)n.Attribute("ID") ?? "",
                (string?)n.Attribute("name") ?? "Untitled notebook"))
            .Where(n => n.Id.Length > 0)
            .ToList();
    }

    public ImportSummary Import(ImportOptions options, CancellationToken cancel)
    {
        var summary = new ImportSummary();

        using var one = new OneNoteInterop();

        Report("Connecting to OneNote…");
        one.Connect();

        Report("Reading notebook structure…");
        var xml = one.GetHierarchy("", OneNoteInterop.ScopePages);

        var doc = XDocument.Parse(xml);
        var ns = doc.Root!.Name.Namespace;

        foreach (var notebook in doc.Root.Elements(ns + "Notebook"))
        {
            cancel.ThrowIfCancellationRequested();

            var id = (string?)notebook.Attribute("ID") ?? "";
            if (options.NotebookIds.Count > 0 && !options.NotebookIds.Contains(id)) continue;

            var name = (string?)notebook.Attribute("name") ?? "Untitled notebook";
            Report($"Notebook: {name}");

            var notebookDir = _workspace.CreateNotebook(name);

            var nbDoc = _workspace.ReadNotebook(notebookDir);
            nbDoc.Source = new SourceDto { Id = id };
            _workspace.WriteNotebook(notebookDir, nbDoc);

            summary.Notebooks++;

            // Section groups nest arbitrarily in OneNote but Scribe's tree is
            // notebook > section, so groups are flattened into the section name
            // with a separator. Nothing is lost, and the path stays readable.
            ImportSectionsUnder(notebook, ns, notebookDir, prefix: "", one, options, summary, cancel);
        }

        Report("Done.");
        return summary;
    }

    private void ImportSectionsUnder(
        XElement parent,
        XNamespace ns,
        string notebookDir,
        string prefix,
        OneNoteInterop one,
        ImportOptions options,
        ImportSummary summary,
        CancellationToken cancel)
    {
        foreach (var section in parent.Elements(ns + "Section"))
        {
            cancel.ThrowIfCancellationRequested();

            var name = (string?)section.Attribute("name") ?? "Untitled section";
            var fullName = prefix.Length > 0 ? $"{prefix} — {name}" : name;

            Report($"  Section: {fullName}");

            var sectionDir = _workspace.CreateSection(notebookDir, fullName);

            var secDoc = _workspace.ReadSection(sectionDir);
            secDoc.Source = new SourceDto { Id = (string?)section.Attribute("ID") };
            _workspace.WriteSection(sectionDir, secDoc);

            summary.Sections++;

            foreach (var page in section.Elements(ns + "Page"))
            {
                cancel.ThrowIfCancellationRequested();
                ImportPage(page, ns, sectionDir, one, options, summary);
            }
        }

        foreach (var group in parent.Elements(ns + "SectionGroup"))
        {
            // OneNote exposes the recycle bin as a section group; skipping it
            // keeps deleted pages from reappearing in the import.
            if ((string?)group.Attribute("isRecycleBin") == "true") continue;

            var groupName = (string?)group.Attribute("name") ?? "Group";
            var nextPrefix = prefix.Length > 0 ? $"{prefix} — {groupName}" : groupName;

            ImportSectionsUnder(group, ns, notebookDir, nextPrefix, one, options, summary, cancel);
        }
    }

    private void ImportPage(
        XElement pageNode,
        XNamespace ns,
        string sectionDir,
        OneNoteInterop one,
        ImportOptions options,
        ImportSummary summary)
    {
        var pageId = (string?)pageNode.Attribute("ID") ?? "";
        var pageName = (string?)pageNode.Attribute("name") ?? "Untitled page";

        try
        {
            Report($"    Page: {pageName}");

            var contentXml = one.GetPageContent(pageId, OneNoteInterop.PageInfoBinaryData);
            var content = XDocument.Parse(contentXml).Root!;

            var doc = new PageDoc
            {
                Title = pageName,
                Source = new SourceDto { Id = pageId },
                Background = new BackgroundDto { Kind = "blank" },
            };

            if (DateTimeOffset.TryParse((string?)pageNode.Attribute("lastModifiedTime"), out var modified))
                doc.Modified = modified;

            if (DateTimeOffset.TryParse((string?)pageNode.Attribute("dateTime"), out var created))
                doc.Created = created;

            ConvertInk(content, ns, doc, summary);
            ConvertText(content, ns, doc);

            if (options.IncludeImages)
                ConvertImages(content, ns, doc, sectionDir, summary);

            var file = _workspace.CreatePage(sectionDir, pageName);
            _workspace.WritePage(file, doc);

            summary.Pages++;
        }
        catch (Exception ex)
        {
            // One unreadable page must not abandon the rest of the import.
            summary.Warnings.Add($"{pageName}: {ex.Message}");
            Report($"    ! Skipped “{pageName}” — {ex.Message}");
        }
    }

    // -------------------------------------------------------------------- ink

    private void ConvertInk(XElement content, XNamespace ns, PageDoc doc, ImportSummary summary)
    {
        // InkDrawing is freeform drawing; InkWord is handwriting OneNote has
        // run recognition over. Both carry the same ISF payload, and both are
        // ink the user drew, so both come across.
        var inkNodes = content.Descendants()
            .Where(e => e.Name == ns + "InkDrawing" || e.Name == ns + "InkWord");

        foreach (var node in inkNodes)
        {
            var data = node.Element(ns + "InkData")?.Value;
            if (string.IsNullOrWhiteSpace(data)) continue;

            StrokeCollection strokes;

            try
            {
                var bytes = Convert.FromBase64String(data.Trim());
                using var stream = new MemoryStream(bytes);
                strokes = new StrokeCollection(stream);
            }
            catch (Exception ex)
            {
                summary.Warnings.Add($"Unreadable ink on “{doc.Title}”: {ex.Message}");
                continue;
            }

            if (strokes.Count == 0) continue;

            PositionStrokes(strokes, node, ns);

            foreach (var stroke in strokes)
            {
                doc.Strokes.Add(StrokeCodec.ToDto(stroke));
                summary.Strokes++;
            }
        }
    }

    /// <summary>
    /// Moves a decoded ink block to where it sat on the OneNote page.
    ///
    /// ISF carries its own coordinate origin, which is unrelated to the page,
    /// so the block is translated by the difference between its own bounds and
    /// the position OneNote recorded for it.
    /// </summary>
    private static void PositionStrokes(StrokeCollection strokes, XElement node, XNamespace ns)
    {
        var position = node.Element(ns + "Position");
        if (position is null) return;

        if (!TryPoints(position.Attribute("x"), out var x) ||
            !TryPoints(position.Attribute("y"), out var y))
        {
            return;
        }

        var bounds = strokes.GetBounds();
        if (bounds.IsEmpty) return;

        var matrix = new Matrix();
        matrix.Translate(x - bounds.X, y - bounds.Y);

        strokes.Transform(matrix, applyToStylusTip: false);

        static bool TryPoints(XAttribute? attribute, out double dips)
        {
            dips = 0;
            if (attribute is null) return false;
            if (!double.TryParse(attribute.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var points))
            {
                return false;
            }

            dips = points * PointsToDip;
            return true;
        }
    }

    // ------------------------------------------------------------------- text

    private static void ConvertText(XElement content, XNamespace ns, PageDoc doc)
    {
        foreach (var outline in content.Descendants(ns + "Outline"))
        {
            var (x, y, width) = ReadBox(outline, ns, defaultWidth: 400);

            var builder = new StringBuilder();

            foreach (var t in outline.Descendants(ns + "T"))
            {
                // Ink elements can carry recognition text; importing it as well
                // would duplicate every handwritten word as typed text.
                if (IsInsideInk(t, ns)) continue;

                var line = HtmlToPlainText(t.Value);
                if (line.Length == 0) continue;

                if (builder.Length > 0) builder.Append('\n');
                builder.Append(line);
            }

            if (builder.Length == 0) continue;

            doc.TextBoxes.Add(new TextBoxDto
            {
                X = x,
                Y = y,
                Width = Math.Max(width, 120),
                Text = builder.ToString(),
            });
        }
    }

    private static bool IsInsideInk(XElement element, XNamespace ns)
    {
        for (var p = element.Parent; p is not null; p = p.Parent)
        {
            if (p.Name == ns + "InkWord" ||
                p.Name == ns + "InkDrawing" ||
                p.Name == ns + "InkParagraph")
            {
                return true;
            }
        }

        return false;
    }

    // ----------------------------------------------------------------- images

    private void ConvertImages(
        XElement content,
        XNamespace ns,
        PageDoc doc,
        string sectionDir,
        ImportSummary summary)
    {
        foreach (var image in content.Descendants(ns + "Image"))
        {
            var data = image.Element(ns + "Data")?.Value;
            if (string.IsNullOrWhiteSpace(data)) continue;

            try
            {
                var bytes = Convert.FromBase64String(data.Trim());
                var format = (string?)image.Attribute("format") ?? "png";
                var relative = _workspace.SaveAsset(sectionDir, bytes, "." + format.ToLowerInvariant());

                var (x, y, width) = ReadBox(image, ns, defaultWidth: 0);
                var height = ReadSize(image, ns).Height;

                doc.Images.Add(new ImageDto
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    File = relative,
                });

                summary.Images++;
            }
            catch (Exception ex)
            {
                summary.Warnings.Add($"Image on “{doc.Title}”: {ex.Message}");
            }
        }
    }

    // ------------------------------------------------------------------ shared

    private static (double X, double Y, double Width) ReadBox(
        XElement element, XNamespace ns, double defaultWidth)
    {
        double x = 0, y = 0, width = defaultWidth;

        var position = element.Element(ns + "Position");
        if (position is not null)
        {
            x = ReadPoints(position.Attribute("x")) ?? 0;
            y = ReadPoints(position.Attribute("y")) ?? 0;
        }

        var size = element.Element(ns + "Size");
        if (size is not null)
            width = ReadPoints(size.Attribute("width")) ?? width;

        return (x, y, width);
    }

    private static (double Width, double Height) ReadSize(XElement element, XNamespace ns)
    {
        var size = element.Element(ns + "Size");
        if (size is null) return (0, 0);

        return (ReadPoints(size.Attribute("width")) ?? 0,
                ReadPoints(size.Attribute("height")) ?? 0);
    }

    private static double? ReadPoints(XAttribute? attribute)
    {
        if (attribute is null) return null;

        if (!double.TryParse(attribute.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var points))
        {
            return null;
        }

        return points * PointsToDip;
    }

    private static readonly Regex TagPattern = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex BreakPattern =
        new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// OneNote wraps run text in inline HTML. Formatting is dropped and the
    /// words are kept, which is the right trade for notes: the text stays
    /// searchable and editable rather than arriving as unreadable markup.
    /// </summary>
    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var text = BreakPattern.Replace(html, "\n");
        text = TagPattern.Replace(text, "");
        text = WebUtility.HtmlDecode(text);

        return text.Replace(' ', ' ').TrimEnd();
    }
}
