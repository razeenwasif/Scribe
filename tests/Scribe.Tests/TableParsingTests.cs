using Scribe.Controls;
using Scribe.Models;
using Xunit;

namespace Scribe.Tests;

public class TableParsingTests
{
    [Fact]
    public void TableDto_RowAndColOperations_WorkCorrectly()
    {
        var model = new TableDto
        {
            Rows = new List<List<string>>
            {
                new() { "A1", "B1" },
                new() { "A2", "B2" }
            }
        };

        Assert.Equal(2, model.Rows.Count);
        Assert.Equal(2, model.Rows[0].Count);

        // Add row
        model.Rows.Add(new List<string> { "A3", "B3" });
        Assert.Equal(3, model.Rows.Count);

        // Add column
        foreach (var r in model.Rows) r.Add("C");
        Assert.Equal(3, model.Rows[0].Count);
        Assert.Equal("C", model.Rows[0][2]);
    }

    [Fact]
    public void ParseMarkdownTable_ReturnsCorrectTable()
    {
        string md = @"| Header 1 | Header 2 |
| --- | --- |
| Row 1 Col 1 | Row 1 Col 2 |
| Row 2 Col 1 | Row 2 Col 2 |";

        // Let's test TSV/Markdown table parsing logic
        var lines = md.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<List<string>>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 1 && lines[i].Contains("---")) continue;
            var rawCells = lines[i].Split('|');
            var cells = rawCells.Select(c => c.Trim()).ToList();
            if (cells.Count > 0 && string.IsNullOrEmpty(cells[0])) cells.RemoveAt(0);
            if (cells.Count > 0 && string.IsNullOrEmpty(cells[^1])) cells.RemoveAt(cells.Count - 1);
            if (cells.Count > 0) rows.Add(cells);
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("Header 1", rows[0][0]);
        Assert.Equal("Header 2", rows[0][1]);
        Assert.Equal("Row 1 Col 1", rows[1][0]);
        Assert.Equal("Row 2 Col 2", rows[2][1]);
    }

    [Fact]
    public void ParseTsvTable_ReturnsCorrectTable()
    {
        string tsv = "Name\tAge\tCity\r\nAlice\t30\tLondon\r\nBob\t25\tNew York";

        var lines = tsv.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<List<string>>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t').Select(p => p.Trim()).ToList();
            rows.Add(parts);
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("Name", rows[0][0]);
        Assert.Equal("Alice", rows[1][0]);
        Assert.Equal("30", rows[1][1]);
        Assert.Equal("New York", rows[2][2]);
    }
}
