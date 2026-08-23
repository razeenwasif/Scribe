using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Scribe.Ink;
using Scribe.Models;
using Scribe.Storage;

namespace Scribe.Controls;

public sealed class PageTextBox : Border
{
    public TextBoxDto Model { get; }
    public RichTextBox Editor { get; }

    private readonly Grid _rootGrid;
    private readonly Thumb _resizeGrip;

    public event EventHandler? ContentChanged;
    public event EventHandler? SelectionStateChanged;
    public event EventHandler? RequestDelete;

    public static readonly FontFamily HandwrittenFontFamily = new("Segoe Print, Ink Free, Segoe Script, Comic Sans MS, cursive");
    public static readonly FontFamily DisplayFontFamily = new("Georgia, Cambria, Segoe UI");
    public static readonly FontFamily DefaultFontFamily = new("Segoe UI Variable Text, Segoe UI");

    public PageTextBox(TextBoxDto model)
    {
        Model = model;

        Background = Brushes.Transparent;
        BorderBrush = Brushes.Transparent;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(0);
        MinWidth = 80;
        MinHeight = 32;

        Width = model.Width > 40 ? model.Width : 360;
        if (model.Height is { } h && h > 20) Height = h;

        _rootGrid = new Grid();

        Editor = new RichTextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Padding = new Thickness(6, 4, 14, 4),
            IsDocumentEnabled = true,
        };

        // Theme-adapted foreground
        var textColor = InkTheme.Adapt(StrokeCodec.FromHex(model.Color, Colors.Black));
        Editor.Foreground = new SolidColorBrush(textColor);
        Editor.CaretBrush = Editor.Foreground;

        InitDocument(model);

        _rootGrid.Children.Add(Editor);

        // Resize handle on the right edge
        _resizeGrip = new Thumb
        {
            Width = 8,
            Cursor = Cursors.SizeWE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            Opacity = 0,
            Background = Brushes.Transparent,
        };
        _resizeGrip.DragDelta += (_, e) =>
        {
            double newWidth = Math.Max(80, Width + e.HorizontalChange);
            Width = newWidth;
            Model.Width = newWidth;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        };
        _rootGrid.Children.Add(_resizeGrip);

        Child = _rootGrid;

        Editor.TextChanged += (_, _) =>
        {
            SyncToModel();
            ContentChanged?.Invoke(this, EventArgs.Empty);
        };

        Editor.SelectionChanged += (_, _) =>
        {
            SelectionStateChanged?.Invoke(this, EventArgs.Empty);
        };

        Editor.GotKeyboardFocus += (_, _) =>
        {
            BorderBrush = (Brush)FindResource("AccentLineBrush");
            _resizeGrip.Opacity = 1;
            SelectionStateChanged?.Invoke(this, EventArgs.Empty);
        };

        Editor.LostKeyboardFocus += (_, _) =>
        {
            BorderBrush = Brushes.Transparent;
            _resizeGrip.Opacity = 0;
        };

        Editor.PreviewKeyDown += OnEditorPreviewKeyDown;

        // Context menu
        var menu = new ContextMenu();
        var cutItem = new MenuItem { Header = "Cut", Command = ApplicationCommands.Cut };
        var copyItem = new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy };
        var pasteItem = new MenuItem { Header = "Paste", Command = ApplicationCommands.Paste };
        menu.Items.Add(cutItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(new Separator());
        var deleteItem = new MenuItem { Header = "Delete container" };
        deleteItem.Click += (_, _) => RequestDelete?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(deleteItem);
        Editor.ContextMenu = menu;
    }

    public string Text
    {
        get
        {
            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            return range.Text.TrimEnd('\r', '\n');
        }
    }

    private void InitDocument(TextBoxDto model)
    {
        Editor.Document.PagePadding = new Thickness(0);
        Editor.Document.Blocks.Clear();

        if (!string.IsNullOrEmpty(model.Xaml))
        {
            try
            {
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(model.Xaml));
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                range.Load(ms, DataFormats.Xaml);
                Editor.Document.PagePadding = new Thickness(0);
                return;
            }
            catch
            {
                // Fall back to plain text
            }
        }

        // Construct document from plain text
        var p = new Paragraph { Margin = new Thickness(0) };
        var run = new Run(model.Text ?? "");

        if (!string.IsNullOrEmpty(model.FontFamily))
            run.FontFamily = new FontFamily(model.FontFamily);
        else
            run.FontFamily = DefaultFontFamily;

        run.FontSize = model.FontSize > 0 ? model.FontSize : 15;
        if (model.Bold) run.FontWeight = FontWeights.Bold;
        if (model.Italic) run.FontStyle = FontStyles.Italic;

        if (model.Underline)
            run.TextDecorations.Add(TextDecorations.Underline);
        if (model.Strikethrough)
            run.TextDecorations.Add(TextDecorations.Strikethrough);

        p.Inlines.Add(run);
        Editor.Document.Blocks.Add(p);
    }

    private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.B)
            {
                ToggleBold();
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                ToggleItalic();
                e.Handled = true;
            }
            else if (e.Key == Key.U)
            {
                ToggleUnderline();
                e.Handled = true;
            }
            else if (e.Key == Key.T || (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && e.Key == Key.X))
            {
                ToggleStrikethrough();
                e.Handled = true;
            }
        }
    }

    // ------------------------------------------------------------- formatting actions

    public void ToggleBold()
    {
        EditingCommands.ToggleBold.Execute(null, Editor);
        SyncToModel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleItalic()
    {
        EditingCommands.ToggleItalic.Execute(null, Editor);
        SyncToModel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleUnderline()
    {
        EditingCommands.ToggleUnderline.Execute(null, Editor);
        SyncToModel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleStrikethrough()
    {
        var selection = Editor.Selection;
        if (selection.IsEmpty)
        {
            // Apply to whole document or current block
            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            ToggleStrikethroughRange(range);
        }
        else
        {
            ToggleStrikethroughRange(selection);
        }

        SyncToModel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ToggleStrikethroughRange(TextRange range)
    {
        var current = range.GetPropertyValue(Inline.TextDecorationsProperty);
        if (current is TextDecorationCollection coll && coll.Contains(TextDecorations.Strikethrough[0]))
        {
            var newColl = new TextDecorationCollection(coll);
            newColl.Remove(TextDecorations.Strikethrough[0]);
            range.ApplyPropertyValue(Inline.TextDecorationsProperty, newColl);
        }
        else
        {
            var newColl = current is TextDecorationCollection c ? new TextDecorationCollection(c) : new TextDecorationCollection();
            newColl.Add(TextDecorations.Strikethrough[0]);
            range.ApplyPropertyValue(Inline.TextDecorationsProperty, newColl);
        }
    }

    public void ApplyFontFamily(string fontName)
    {
        FontFamily family = fontName.ToLowerInvariant() switch
        {
            "handwritten" or "segoe print" or "ink free" => HandwrittenFontFamily,
            "display" or "georgia" => DisplayFontFamily,
            "monospace" or "consolas" or "cascadia code" => new FontFamily("Cascadia Code, Consolas, Courier New"),
            "arial" => new FontFamily("Arial"),
            "times new roman" => new FontFamily("Times New Roman"),
            _ => new FontFamily(fontName),
        };

        var target = Editor.Selection.IsEmpty
            ? new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
            : Editor.Selection;

        target.ApplyPropertyValue(TextElement.FontFamilyProperty, family);
        Model.FontFamily = fontName;

        SyncToModel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyFontSize(double size)
    {
        if (size <= 0) return;

        var target = Editor.Selection.IsEmpty
            ? new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
            : Editor.Selection;

        target.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        Model.FontSize = size;

        SyncToModel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyHeadingStyle(string style)
    {
        var target = Editor.Selection.IsEmpty
            ? new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
            : Editor.Selection;

        Model.Heading = style.ToLowerInvariant();

        switch (Model.Heading)
        {
            case "title":
                target.ApplyPropertyValue(TextElement.FontFamilyProperty, DisplayFontFamily);
                target.ApplyPropertyValue(TextElement.FontSizeProperty, 24.0);
                target.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                target.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                Model.FontSize = 24;
                Model.FontFamily = "Georgia";
                Model.Bold = true;
                break;

            case "h1":
                target.ApplyPropertyValue(TextElement.FontFamilyProperty, DefaultFontFamily);
                target.ApplyPropertyValue(TextElement.FontSizeProperty, 20.0);
                target.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
                target.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                Model.FontSize = 20;
                Model.Bold = true;
                break;

            case "h2":
                target.ApplyPropertyValue(TextElement.FontFamilyProperty, DefaultFontFamily);
                target.ApplyPropertyValue(TextElement.FontSizeProperty, 16.0);
                target.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
                target.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                Model.FontSize = 16;
                Model.Bold = true;
                break;

            case "h3":
                target.ApplyPropertyValue(TextElement.FontFamilyProperty, DefaultFontFamily);
                target.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
                target.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.SemiBold);
                target.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                Model.FontSize = 14;
                Model.Bold = true;
                break;

            case "handwritten":
                target.ApplyPropertyValue(TextElement.FontFamilyProperty, HandwrittenFontFamily);
                target.ApplyPropertyValue(TextElement.FontSizeProperty, 16.0);
                target.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                target.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                Model.FontSize = 16;
                Model.FontFamily = "Handwritten";
                break;

            default: // "body"
                target.ApplyPropertyValue(TextElement.FontFamilyProperty, DefaultFontFamily);
                target.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
                target.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
                target.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
                Model.FontSize = 14;
                Model.Bold = false;
                Model.Italic = false;
                break;
        }

        SyncToModel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
        SelectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public (bool Bold, bool Italic, bool Underline, bool Strikethrough, string FontFamily, double FontSize, string Heading) GetFormattingState()
    {
        var target = Editor.Selection.IsEmpty
            ? new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd)
            : Editor.Selection;

        var weight = target.GetPropertyValue(TextElement.FontWeightProperty);
        bool bold = weight is FontWeight fw && (fw == FontWeights.Bold || fw == FontWeights.SemiBold || fw == FontWeights.ExtraBold);

        var style = target.GetPropertyValue(TextElement.FontStyleProperty);
        bool italic = style is FontStyle fs && (fs == FontStyles.Italic || fs == FontStyles.Oblique);

        var decors = target.GetPropertyValue(Inline.TextDecorationsProperty);
        bool underline = false;
        bool strikethrough = false;
        if (decors is TextDecorationCollection coll)
        {
            underline = coll.Contains(TextDecorations.Underline[0]);
            strikethrough = coll.Contains(TextDecorations.Strikethrough[0]);
        }

        var familyProp = target.GetPropertyValue(TextElement.FontFamilyProperty);
        string family = (familyProp as FontFamily)?.Source ?? Model.FontFamily ?? "Segoe UI";

        var sizeProp = target.GetPropertyValue(TextElement.FontSizeProperty);
        double size = sizeProp is double d ? d : Model.FontSize;

        string heading = Model.Heading ?? "body";

        return (bold, italic, underline, strikethrough, family, size, heading);
    }

    public void SyncToModel()
    {
        Model.Text = Text;
        Model.X = InkCanvas.GetLeft(this);
        Model.Y = InkCanvas.GetTop(this);
        Model.Width = double.IsNaN(Width) ? ActualWidth : Width;
        Model.Height = double.IsNaN(Height) ? null : Height;

        if (double.IsNaN(Model.X)) Model.X = 0;
        if (double.IsNaN(Model.Y)) Model.Y = 0;

        // Save XAML flow document
        try
        {
            var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.Xaml);
            Model.Xaml = Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            // Fall back
        }
    }
}
