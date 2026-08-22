using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Scribe.Models;

namespace Scribe.Controls;

/// <summary>
/// A free-floating text container, the typed counterpart to ink. Mirrors
/// OneNote's model: click anywhere on the page and a container appears there,
/// growing downward as you type rather than reflowing the whole page.
/// </summary>
public sealed class PageTextBox : TextBox
{
    public TextBoxDto Model { get; }

    public PageTextBox(TextBoxDto model)
    {
        Model = model;

        AcceptsReturn = true;
        AcceptsTab = false;
        TextWrapping = TextWrapping.Wrap;
        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Transparent;
        Background = Brushes.Transparent;
        Padding = new Thickness(4, 2, 4, 2);
        MinWidth = 60;
        MinHeight = 24;

        Text = model.Text;
        Width = model.Width;
        if (model.Height is { } h && h > 0) Height = h;

        FontFamily = new FontFamily(model.FontFamily);
        FontSize = model.FontSize;
        FontWeight = model.Bold ? FontWeights.Bold : FontWeights.Normal;
        FontStyle = model.Italic ? FontStyles.Italic : FontStyles.Normal;
        // Adapted for the current theme, exactly as ink is: typed notes stored
        // as black must still be readable on a dark page.
        Foreground = new SolidColorBrush(
            Ink.InkTheme.Adapt(Storage.StrokeCodec.FromHex(model.Color, Colors.Black)));

        CaretBrush = Foreground;

        // A visible frame only while the container is being worked on, so a
        // finished page reads as writing rather than as a form.
        GotKeyboardFocus += (_, _) => BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0xC6, 0xDA));
        LostKeyboardFocus += (_, _) => BorderBrush = Brushes.Transparent;
    }

    /// <summary>Copies live control state back into the serialisable model.</summary>
    public void SyncToModel()
    {
        Model.Text = Text;
        Model.X = InkCanvas.GetLeft(this);
        Model.Y = InkCanvas.GetTop(this);
        Model.Width = double.IsNaN(Width) ? ActualWidth : Width;
        Model.Height = double.IsNaN(Height) ? null : Height;
    }
}
