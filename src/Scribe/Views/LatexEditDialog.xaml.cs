using System.Windows;
using System.Windows.Controls;

namespace Scribe.Views;

public partial class LatexEditDialog : Window
{
    public string ResultLatex { get; private set; } = "";
    public double ResultScale { get; private set; } = 22;

    public LatexEditDialog(string initialLatex = "", double initialScale = 22, bool isEditing = false)
    {
        InitializeComponent();

        if (string.IsNullOrWhiteSpace(initialLatex))
        {
            initialLatex = @"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}";
        }

        ResultScale = initialScale > 0 ? initialScale : 22;
        ScaleSlider.Value = ResultScale;
        ScaleLabel.Text = $"{ResultScale:0} pt";

        FormulaInput.Text = initialLatex;
        FormulaInput.SelectAll();

        if (isEditing)
        {
            OkButton.Content = "Update Equation";
            Title = "Edit LaTeX Equation";
        }

        Loaded += (_, _) => FormulaInput.Focus();
        UpdatePreview();
    }

    private void FormulaInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void Snippet_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string snippet)
        {
            int caret = FormulaInput.SelectionStart;
            FormulaInput.Text = FormulaInput.Text.Insert(caret, snippet);
            FormulaInput.SelectionStart = caret + snippet.Length;
            FormulaInput.Focus();
        }
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        ResultScale = e.NewValue;
        ScaleLabel.Text = $"{ResultScale:0} pt";
        FormulaPreview.Scale = ResultScale;
    }

    private void UpdatePreview()
    {
        var text = FormulaInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            FormulaPreview.Formula = "";
            ErrorText.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            FormulaPreview.Formula = text;
            FormulaPreview.Scale = ResultScale;
            FormulaPreview.Visibility = Visibility.Visible;
            ErrorText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            FormulaPreview.Visibility = Visibility.Collapsed;
            ErrorText.Text = $"Syntax Error: {ex.Message}";
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var latex = FormulaInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(latex))
        {
            MessageBox.Show(this, "Please enter a LaTeX formula.", "Empty Formula", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ResultLatex = latex;
        ResultScale = ScaleSlider.Value;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
