using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Scribe.Controls;

/// <summary>
/// A single-field prompt, for naming things. Built in code rather than XAML
/// because it is small and needs no styling beyond the app's own resources.
/// </summary>
public sealed class PromptDialog : Window
{
    private readonly TextBox _input;

    public string Value => _input.Text.Trim();

    public PromptDialog(Window owner, string title, string label, string initial = "")
    {
        Owner = owner;
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["ChromeBrush"];

        // Foreground inherits to every label below, which is how the themed
        // text colour reaches controls that do not set one.
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"];

        _input = new TextBox
        {
            Text = initial,
            Padding = new Thickness(7, 5, 7, 5),
            FontSize = 14,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var ok = new Button
        {
            Content = "OK",
            IsDefault = true,
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(8, 0, 0, 0),
        };
        ok.Click += (_, _) => Close(true);

        var cancel = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            Padding = new Thickness(14, 6, 14, 6),
        };
        cancel.Click += (_, _) => Close(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Opacity = 0.75 });
        panel.Children.Add(_input);
        panel.Children.Add(buttons);

        Content = panel;

        // Open with the existing name selected, so typing replaces it.
        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    private void Close(bool result)
    {
        DialogResult = result;
        base.Close();
    }

    /// <summary>Returns the entered text, or null if cancelled or left blank.</summary>
    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var dlg = new PromptDialog(owner, title, label, initial);
        if (dlg.ShowDialog() != true) return null;
        return string.IsNullOrWhiteSpace(dlg.Value) ? null : dlg.Value;
    }
}
