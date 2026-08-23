using System.Text.Json;
using Scribe.Models;
using Scribe.Storage;
using Xunit;

namespace Scribe.Tests;

public class PageDocTests
{
    [Fact]
    public void PageDoc_RoundTrips_AllNewFeatures()
    {
        var doc = new PageDoc
        {
            Title = "Math & Research Notes",
            Background = new BackgroundDto { Kind = "grid", Spacing = 24 },
            TextBoxes = new List<TextBoxDto>
            {
                new()
                {
                    X = 50,
                    Y = 50,
                    Text = "Handwritten Header",
                    FontFamily = "Handwritten",
                    FontSize = 24,
                    Bold = true,
                    Italic = true,
                    Underline = true,
                    Strikethrough = false,
                    Heading = "title",
                    Xaml = "<FlowDocument xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Paragraph><Run FontWeight=\"Bold\">Handwritten Header</Run></Paragraph></FlowDocument>"
                }
            },
            LatexBlocks = new List<LatexDto>
            {
                new()
                {
                    X = 60,
                    Y = 150,
                    Latex = @"\int_{0}^{\infty} e^{-x^2} \, dx = \frac{\sqrt{\pi}}{2}",
                    Scale = 26,
                    Color = "#1D4ED8"
                }
            },
            Tables = new List<TableDto>
            {
                new()
                {
                    X = 60,
                    Y = 300,
                    HasHeader = true,
                    Rows = new List<List<string>>
                    {
                        new() { "Item", "Quantity", "Price" },
                        new() { "Apples", "10", "$5.00" },
                        new() { "Oranges", "8", "$4.20" }
                    },
                    ColumnWidths = new List<double> { 150, 100, 120 }
                }
            },
            Images = new List<ImageDto>
            {
                new()
                {
                    X = 60,
                    Y = 500,
                    Width = 400,
                    Height = 300,
                    File = "assets/diagram.png"
                }
            }
        };

        var json = JsonSerializer.Serialize(doc, Workspace.Json);
        var restored = JsonSerializer.Deserialize<PageDoc>(json, Workspace.Json);

        Assert.NotNull(restored);
        Assert.Equal("Math & Research Notes", restored.Title);
        Assert.Equal("grid", restored.Background.Kind);

        // Check TextBoxes
        Assert.Single(restored.TextBoxes);
        var tb = restored.TextBoxes[0];
        Assert.Equal("Handwritten Header", tb.Text);
        Assert.Equal("Handwritten", tb.FontFamily);
        Assert.Equal(24, tb.FontSize);
        Assert.True(tb.Bold);
        Assert.True(tb.Italic);
        Assert.True(tb.Underline);
        Assert.False(tb.Strikethrough);
        Assert.Equal("title", tb.Heading);
        Assert.NotNull(tb.Xaml);

        // Check LaTeX
        Assert.Single(restored.LatexBlocks);
        var lx = restored.LatexBlocks[0];
        Assert.Equal(@"\int_{0}^{\infty} e^{-x^2} \, dx = \frac{\sqrt{\pi}}{2}", lx.Latex);
        Assert.Equal(26, lx.Scale);
        Assert.Equal("#1D4ED8", lx.Color);

        // Check Tables
        Assert.Single(restored.Tables);
        var tbl = restored.Tables[0];
        Assert.True(tbl.HasHeader);
        Assert.Equal(3, tbl.Rows.Count);
        Assert.Equal("Item", tbl.Rows[0][0]);
        Assert.Equal("Apples", tbl.Rows[1][0]);
        Assert.Equal("$4.20", tbl.Rows[2][2]);
        Assert.Equal(3, tbl.ColumnWidths?.Count);

        // Check Images
        Assert.Single(restored.Images);
        Assert.Equal("assets/diagram.png", restored.Images[0].File);
    }

    [Fact]
    public void PageDoc_BackwardsCompatible_WithLegacyJson()
    {
        string legacyJson = @"
        {
            ""schema"": ""scribe.page/1"",
            ""id"": ""legacy-page-123"",
            ""title"": ""Old Page"",
            ""strokes"": [],
            ""textBoxes"": [
                {
                    ""id"": ""tb-1"",
                    ""x"": 10,
                    ""y"": 20,
                    ""text"": ""Old Plain Text"",
                    ""fontFamily"": ""Segoe UI"",
                    ""fontSize"": 15,
                    ""bold"": false,
                    ""italic"": false
                }
            ],
            ""images"": []
        }";

        var doc = JsonSerializer.Deserialize<PageDoc>(legacyJson, Workspace.Json);
        Assert.NotNull(doc);
        Assert.Equal("Old Page", doc.Title);
        Assert.Single(doc.TextBoxes);
        Assert.Equal("Old Plain Text", doc.TextBoxes[0].Text);
        Assert.False(doc.TextBoxes[0].Underline);
        Assert.False(doc.TextBoxes[0].Strikethrough);
        Assert.Null(doc.TextBoxes[0].Heading);
        Assert.Null(doc.TextBoxes[0].Xaml);
        Assert.Empty(doc.LatexBlocks);
        Assert.Empty(doc.Tables);
        Assert.Empty(doc.Images);
    }
}
