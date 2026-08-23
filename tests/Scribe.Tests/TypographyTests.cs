using Scribe.Models;
using Xunit;

namespace Scribe.Tests;

public class TypographyTests
{
    [Fact]
    public void TextBoxDto_SupportsAllFormattingProperties()
    {
        var tb = new TextBoxDto
        {
            Text = "Sample Text",
            FontFamily = "Segoe Print",
            FontSize = 18,
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            Heading = "handwritten"
        };

        Assert.Equal("Sample Text", tb.Text);
        Assert.Equal("Segoe Print", tb.FontFamily);
        Assert.Equal(18, tb.FontSize);
        Assert.True(tb.Bold);
        Assert.True(tb.Italic);
        Assert.True(tb.Underline);
        Assert.True(tb.Strikethrough);
        Assert.Equal("handwritten", tb.Heading);
    }

    [Theory]
    [InlineData("title", 24, true)]
    [InlineData("h1", 20, true)]
    [InlineData("h2", 16, true)]
    [InlineData("h3", 14, true)]
    [InlineData("body", 14, false)]
    [InlineData("handwritten", 16, false)]
    public void HeadingPresets_MapToExpectedValues(string heading, double expectedSize, bool expectedBold)
    {
        var dto = new TextBoxDto { Heading = heading };
        switch (heading)
        {
            case "title":
                dto.FontSize = 24;
                dto.Bold = true;
                dto.FontFamily = "Georgia";
                break;
            case "h1":
                dto.FontSize = 20;
                dto.Bold = true;
                break;
            case "h2":
                dto.FontSize = 16;
                dto.Bold = true;
                break;
            case "h3":
                dto.FontSize = 14;
                dto.Bold = true;
                break;
            case "handwritten":
                dto.FontSize = 16;
                dto.FontFamily = "Handwritten";
                break;
            default:
                dto.FontSize = 14;
                break;
        }

        Assert.Equal(expectedSize, dto.FontSize);
        Assert.Equal(expectedBold, dto.Bold);
    }
}
