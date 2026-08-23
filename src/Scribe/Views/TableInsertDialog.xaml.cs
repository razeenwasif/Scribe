using System.Windows;

namespace Scribe.Views;

public partial class TableInsertDialog : Window
{
    public int Columns { get; private set; } = 3;
    public int Rows { get; private set; } = 3;
    public bool HasHeader { get; private set; } = true;

    public TableInsertDialog()
    {
        InitializeComponent();
        UpdateLabel();
    }

    private void ColsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        Columns = (int)ColsSlider.Value;
        UpdateLabel();
    }

    private void RowsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        Rows = (int)RowsSlider.Value;
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        Columns = (int)ColsSlider.Value;
        Rows = (int)RowsSlider.Value;
        DimensionLabel.Text = $"{Columns} column{(Columns == 1 ? "" : "s")} × {Rows} row{(Rows == 1 ? "" : "s")}";
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        Columns = (int)ColsSlider.Value;
        Rows = (int)RowsSlider.Value;
        HasHeader = HeaderCheck.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
