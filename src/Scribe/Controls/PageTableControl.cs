using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scribe.Ink;
using Scribe.Models;
using Scribe.Storage;

namespace Scribe.Controls;

public sealed class PageTableControl : Border
{
    public TableDto Model { get; }

    private readonly Grid _grid;
    private readonly Border _dragBar;
    private readonly StackPanel _rootPanel;
    private readonly List<List<TextBox>> _cellBoxes = new();

    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _initialCanvasPos;

    public event EventHandler? Changed;
    public event EventHandler? RequestDelete;

    public PageTableControl(TableDto model)
    {
        Model = model;

        if (Model.Rows.Count == 0)
        {
            Model.Rows.Add(new List<string> { "Header 1", "Header 2", "Header 3" });
            Model.Rows.Add(new List<string> { "", "", "" });
            Model.Rows.Add(new List<string> { "", "", "" });
        }

        Background = Brushes.Transparent;
        BorderBrush = (Brush)FindResource("BorderBrush");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        Margin = new Thickness(0);

        _rootPanel = new StackPanel { Orientation = Orientation.Vertical };

        // Top drag and control bar
        _dragBar = new Border
        {
            Height = 18,
            Background = (Brush)FindResource("ElevatedBrush"),
            CornerRadius = new CornerRadius(5, 5, 0, 0),
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to move table",
        };

        var dragIndicator = new Border
        {
            Width = 36,
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = (Brush)FindResource("FaintTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _dragBar.Child = dragIndicator;

        _dragBar.MouseLeftButtonDown += OnDragBarMouseDown;
        _dragBar.MouseMove += OnDragBarMouseMove;
        _dragBar.MouseLeftButtonUp += OnDragBarMouseUp;

        _grid = new Grid
        {
            Background = (Brush)FindResource("PageSurfaceBrush"),
        };

        _rootPanel.Children.Add(_dragBar);
        _rootPanel.Children.Add(_grid);
        Child = _rootPanel;

        RebuildGrid();
    }

    public void RebuildGrid()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _cellBoxes.Clear();

        int rowCount = Model.Rows.Count;
        if (rowCount == 0) return;
        int colCount = Model.Rows.Max(r => r.Count);
        if (colCount == 0) return;

        // Ensure all rows have equal columns
        for (int r = 0; r < rowCount; r++)
        {
            while (Model.Rows[r].Count < colCount)
                Model.Rows[r].Add("");
        }

        for (int c = 0; c < colCount; c++)
        {
            double width = 120;
            if (Model.ColumnWidths is not null && c < Model.ColumnWidths.Count && Model.ColumnWidths[c] > 30)
                width = Model.ColumnWidths[c];

            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Pixel) });
        }

        for (int r = 0; r < rowCount; r++)
        {
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var boxRow = new List<TextBox>();

            for (int c = 0; c < colCount; c++)
            {
                int curRow = r;
                int curCol = c;
                bool isHeader = (r == 0 && Model.HasHeader);

                var cellBorder = new Border
                {
                    BorderBrush = (Brush)FindResource("SubtleBorderBrush"),
                    BorderThickness = new Thickness(
                        c == 0 ? 0 : 1,
                        r == 0 ? 0 : 1,
                        0,
                        0),
                    Background = isHeader ? (Brush)FindResource("HoverBrush") : Brushes.Transparent,
                };

                var tb = new TextBox
                {
                    Text = Model.Rows[r][c],
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = (Brush)FindResource("TextBrush"),
                    FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                    FontSize = 13,
                    Padding = new Thickness(8, 6, 8, 6),
                    AcceptsReturn = false,
                    TextWrapping = TextWrapping.Wrap,
                    MinWidth = 60,
                };

                tb.Tag = (curRow, curCol);
                tb.TextChanged += (_, _) =>
                {
                    Model.Rows[curRow][curCol] = tb.Text;
                    Changed?.Invoke(this, EventArgs.Empty);
                };

                tb.KeyDown += (s, e) => OnCellKeyDown(s, e, curRow, curCol);

                // Context menu for each cell
                tb.ContextMenu = CreateCellContextMenu(curRow, curCol);

                cellBorder.Child = tb;
                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, c);
                _grid.Children.Add(cellBorder);

                boxRow.Add(tb);
            }
            _cellBoxes.Add(boxRow);
        }
    }

    private ContextMenu CreateCellContextMenu(int row, int col)
    {
        var menu = new ContextMenu();

        var addRowAbove = new MenuItem { Header = "Insert row above" };
        addRowAbove.Click += (_, _) => InsertRow(row);
        menu.Items.Add(addRowAbove);

        var addRowBelow = new MenuItem { Header = "Insert row below" };
        addRowBelow.Click += (_, _) => InsertRow(row + 1);
        menu.Items.Add(addRowBelow);

        var deleteRow = new MenuItem { Header = "Delete row" };
        deleteRow.Click += (_, _) => DeleteRow(row);
        deleteRow.IsEnabled = Model.Rows.Count > 1;
        menu.Items.Add(deleteRow);

        menu.Items.Add(new Separator());

        var addColLeft = new MenuItem { Header = "Insert column left" };
        addColLeft.Click += (_, _) => InsertColumn(col);
        menu.Items.Add(addColLeft);

        var addColRight = new MenuItem { Header = "Insert column right" };
        addColRight.Click += (_, _) => InsertColumn(col + 1);
        menu.Items.Add(addColRight);

        var deleteCol = new MenuItem { Header = "Delete column" };
        deleteCol.Click += (_, _) => DeleteColumn(col);
        deleteCol.IsEnabled = Model.Rows.Count > 0 && Model.Rows[0].Count > 1;
        menu.Items.Add(deleteCol);

        menu.Items.Add(new Separator());

        var copyTable = new MenuItem { Header = "Copy table" };
        copyTable.Click += (_, _) => CopyToClipboard();
        menu.Items.Add(copyTable);

        var toggleHeader = new MenuItem { Header = Model.HasHeader ? "Disable header row" : "Enable header row" };
        toggleHeader.Click += (_, _) =>
        {
            Model.HasHeader = !Model.HasHeader;
            RebuildGrid();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(toggleHeader);

        menu.Items.Add(new Separator());

        var deleteTable = new MenuItem { Header = "Delete table" };
        deleteTable.Click += (_, _) => RequestDelete?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(deleteTable);

        return menu;
    }

    private void OnCellKeyDown(object sender, KeyEventArgs e, int row, int col)
    {
        if (e.Key == Key.Tab)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                // Shift+Tab -> Move back
                if (col > 0) FocusCell(row, col - 1);
                else if (row > 0) FocusCell(row - 1, Model.Rows[row - 1].Count - 1);
            }
            else
            {
                // Tab -> Move forward. If last cell, add new row!
                if (col < Model.Rows[row].Count - 1)
                {
                    FocusCell(row, col + 1);
                }
                else if (row < Model.Rows.Count - 1)
                {
                    FocusCell(row + 1, 0);
                }
                else
                {
                    // Add new row at end
                    InsertRow(Model.Rows.Count);
                    FocusCell(Model.Rows.Count - 1, 0);
                }
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Enter -> Move to row below
            if (row < Model.Rows.Count - 1)
            {
                FocusCell(row + 1, col);
            }
            else
            {
                InsertRow(Model.Rows.Count);
                FocusCell(Model.Rows.Count - 1, col);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Up && row > 0 && sender is TextBox tbUp && tbUp.CaretIndex == 0)
        {
            FocusCell(row - 1, col);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && row < Model.Rows.Count - 1 && sender is TextBox tbDown && tbDown.CaretIndex == tbDown.Text.Length)
        {
            FocusCell(row + 1, col);
            e.Handled = true;
        }
    }

    public void FocusCell(int row, int col)
    {
        if (row >= 0 && row < _cellBoxes.Count && col >= 0 && col < _cellBoxes[row].Count)
        {
            var tb = _cellBoxes[row][col];
            tb.Focus();
            tb.SelectAll();
        }
    }

    public void InsertRow(int atIndex)
    {
        int colCount = Model.Rows.Count > 0 ? Model.Rows[0].Count : 3;
        var newRow = new List<string>(colCount);
        for (int i = 0; i < colCount; i++) newRow.Add("");

        atIndex = Math.Clamp(atIndex, 0, Model.Rows.Count);
        Model.Rows.Insert(atIndex, newRow);

        RebuildGrid();
        Changed?.Invoke(this, EventArgs.Empty);
        FocusCell(atIndex, 0);
    }

    public void DeleteRow(int index)
    {
        if (Model.Rows.Count <= 1 || index < 0 || index >= Model.Rows.Count) return;

        Model.Rows.RemoveAt(index);
        RebuildGrid();
        Changed?.Invoke(this, EventArgs.Empty);

        int nextRow = Math.Min(index, Model.Rows.Count - 1);
        FocusCell(nextRow, 0);
    }

    public void InsertColumn(int atIndex)
    {
        for (int r = 0; r < Model.Rows.Count; r++)
        {
            int colIdx = Math.Clamp(atIndex, 0, Model.Rows[r].Count);
            Model.Rows[r].Insert(colIdx, "");
        }

        if (Model.ColumnWidths is not null)
        {
            int colIdx = Math.Clamp(atIndex, 0, Model.ColumnWidths.Count);
            Model.ColumnWidths.Insert(colIdx, 120);
        }

        RebuildGrid();
        Changed?.Invoke(this, EventArgs.Empty);
        FocusCell(0, atIndex);
    }

    public void DeleteColumn(int index)
    {
        if (Model.Rows.Count == 0 || Model.Rows[0].Count <= 1) return;

        for (int r = 0; r < Model.Rows.Count; r++)
        {
            if (index >= 0 && index < Model.Rows[r].Count)
                Model.Rows[r].RemoveAt(index);
        }

        if (Model.ColumnWidths is not null && index >= 0 && index < Model.ColumnWidths.Count)
            Model.ColumnWidths.RemoveAt(index);

        RebuildGrid();
        Changed?.Invoke(this, EventArgs.Empty);
        int nextCol = Math.Min(index, Model.Rows[0].Count - 1);
        FocusCell(0, nextCol);
    }

    public void CopyToClipboard()
    {
        CopyTableToClipboard(Model);
    }

    public static void CopyTableToClipboard(TableDto table)
    {
        if (table.Rows.Count == 0) return;

        // TSV
        var tsv = new StringBuilder();
        foreach (var row in table.Rows)
            tsv.AppendLine(string.Join("\t", row));

        // HTML
        var html = new StringBuilder();
        html.AppendLine("<table>");
        for (int r = 0; r < table.Rows.Count; r++)
        {
            html.AppendLine("  <tr>");
            bool isHeader = (r == 0 && table.HasHeader);
            string tag = isHeader ? "th" : "td";
            foreach (var cell in table.Rows[r])
            {
                var escaped = System.Net.WebUtility.HtmlEncode(cell);
                html.AppendLine($"    <{tag}>{escaped}</{tag}>");
            }
            html.AppendLine("  </tr>");
        }
        html.AppendLine("</table>");

        var dataObj = new DataObject();
        dataObj.SetData(DataFormats.Text, tsv.ToString());
        dataObj.SetData(DataFormats.UnicodeText, tsv.ToString());
        dataObj.SetData(DataFormats.Html, WrapHtmlClipboardData(html.ToString()));

        Clipboard.SetDataObject(dataObj, true);
    }

    public static TableDto? TryParseClipboardTable()
    {
        try
        {
            if (Clipboard.ContainsText(TextDataFormat.Html))
            {
                var html = Clipboard.GetText(TextDataFormat.Html);
                var table = ParseHtmlTable(html);
                if (table is not null && table.Rows.Count > 0) return table;
            }

            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                var table = ParseTsvOrMarkdownTable(text);
                if (table is not null && table.Rows.Count > 0) return table;
            }
        }
        catch
        {
            // Clipboard access error
        }

        return null;
    }

    private static TableDto? ParseHtmlTable(string html)
    {
        var rows = new List<List<string>>();
        var trMatches = Regex.Matches(html, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (trMatches.Count == 0) return null;

        bool hasHeader = false;
        for (int r = 0; r < trMatches.Count; r++)
        {
            var rowHtml = trMatches[r].Groups[1].Value;
            var cellMatches = Regex.Matches(rowHtml, @"<(th|td)[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (cellMatches.Count == 0) continue;

            if (r == 0 && cellMatches[0].Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase))
                hasHeader = true;

            var row = new List<string>();
            foreach (Match cm in cellMatches)
            {
                var cellContent = Regex.Replace(cm.Groups[2].Value, @"<[^>]+>", "").Trim();
                cellContent = System.Net.WebUtility.HtmlDecode(cellContent);
                row.Add(cellContent);
            }
            rows.Add(row);
        }

        if (rows.Count == 0) return null;
        return new TableDto { Rows = rows, HasHeader = hasHeader };
    }

    private static TableDto? ParseTsvOrMarkdownTable(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        // Check for Markdown table: | col1 | col2 |
        if (lines.Length >= 2 && lines[0].Contains('|') && lines[1].Contains("---"))
        {
            var rows = new List<List<string>>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 1 && lines[i].Contains("---")) continue; // separator line

                var rawCells = lines[i].Split('|');
                var cells = rawCells.Select(c => c.Trim()).ToList();
                // strip leading/trailing empty cells from outer pipes
                if (cells.Count > 0 && string.IsNullOrEmpty(cells[0])) cells.RemoveAt(0);
                if (cells.Count > 0 && string.IsNullOrEmpty(cells[^1])) cells.RemoveAt(cells.Count - 1);

                if (cells.Count > 0) rows.Add(cells);
            }
            if (rows.Count > 0)
                return new TableDto { Rows = rows, HasHeader = true };
        }

        // Check for TSV or CSV (lines containing Tab)
        bool hasTabs = lines.Any(l => l.Contains('\t'));
        if (hasTabs || lines.Length > 1)
        {
            char separator = hasTabs ? '\t' : (lines[0].Contains(',') ? ',' : '\t');
            if (!hasTabs && !lines[0].Contains(',')) return null; // regular plain text, not a table

            var rows = new List<List<string>>();
            foreach (var line in lines)
            {
                var parts = line.Split(separator).Select(p => p.Trim()).ToList();
                rows.Add(parts);
            }

            int maxCols = rows.Max(r => r.Count);
            if (maxCols >= 2 || (rows.Count >= 2 && maxCols >= 1))
            {
                return new TableDto { Rows = rows, HasHeader = true };
            }
        }

        return null;
    }

    private static string WrapHtmlClipboardData(string html)
    {
        string header =
            "Version:0.9\r\n" +
            "StartHTML:<<<<<<<<1\r\n" +
            "EndHTML:<<<<<<<<2\r\n" +
            "StartFragment:<<<<<<<<3\r\n" +
            "EndFragment:<<<<<<<<4\r\n";

        string startHtml = "<html><body><!--StartFragment-->";
        string endHtml = "<!--EndFragment--></body></html>";

        string full = header + startHtml + html + endHtml;
        int startHtmlPos = header.Length;
        int startFragPos = header.Length + startHtml.Length;
        int endFragPos = startFragPos + Encoding.UTF8.GetByteCount(html);
        int endHtmlPos = endFragPos + endHtml.Length;

        return header
            .Replace("<<<<<<<<1", startHtmlPos.ToString("D8"))
            .Replace("<<<<<<<<2", endHtmlPos.ToString("D8"))
            .Replace("<<<<<<<<3", startFragPos.ToString("D8"))
            .Replace("<<<<<<<<4", endFragPos.ToString("D8"))
            + startHtml + html + endHtml;
    }

    // ------------------------------------------------------------- dragging table

    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = e.GetPosition(Parent as IInputElement);
        _initialCanvasPos = new Point(InkCanvas.GetLeft(this), InkCanvas.GetTop(this));
        if (double.IsNaN(_initialCanvasPos.X)) _initialCanvasPos.X = 0;
        if (double.IsNaN(_initialCanvasPos.Y)) _initialCanvasPos.Y = 0;

        _dragBar.CaptureMouse();
        BorderBrush = (Brush)FindResource("AccentLineBrush");
        e.Handled = true;
    }

    private void OnDragBarMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || Parent is not UIElement parentEl) return;

        var currentPoint = e.GetPosition(parentEl);
        double deltaX = currentPoint.X - _dragStartPoint.X;
        double deltaY = currentPoint.Y - _dragStartPoint.Y;

        double newLeft = Math.Max(0, _initialCanvasPos.X + deltaX);
        double newTop = Math.Max(0, _initialCanvasPos.Y + deltaY);

        InkCanvas.SetLeft(this, newLeft);
        InkCanvas.SetTop(this, newTop);

        e.Handled = true;
    }

    private void OnDragBarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _dragBar.ReleaseMouseCapture();
            BorderBrush = (Brush)FindResource("BorderBrush");
            SyncToModel();
            Changed?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    public void SyncToModel()
    {
        Model.X = InkCanvas.GetLeft(this);
        Model.Y = InkCanvas.GetTop(this);
        if (double.IsNaN(Model.X)) Model.X = 0;
        if (double.IsNaN(Model.Y)) Model.Y = 0;

        for (int r = 0; r < _cellBoxes.Count && r < Model.Rows.Count; r++)
        {
            for (int c = 0; c < _cellBoxes[r].Count && c < Model.Rows[r].Count; c++)
            {
                Model.Rows[r][c] = _cellBoxes[r][c].Text;
            }
        }
    }
}
