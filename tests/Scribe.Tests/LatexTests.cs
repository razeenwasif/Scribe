using Scribe.Models;
using WpfMath.Controls;
using Xunit;

namespace Scribe.Tests;

public class LatexTests
{
    [Theory]
    [InlineData(@"x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}")]
    [InlineData(@"\int_{0}^{\infty} e^{-x^2} \, dx = \frac{\sqrt{\pi}}{2}")]
    [InlineData(@"\sum_{n=1}^{\infty} \frac{1}{n^2} = \frac{\pi^2}{6}")]
    [InlineData(@"E = mc^2")]
    [InlineData(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}")]
    public void FormulaControl_AcceptsValidLatexFormulas(string formula)
    {
        var control = new FormulaControl { Formula = formula, Scale = 22 };
        Assert.Equal(formula, control.Formula);
        Assert.Equal(22, control.Scale);
    }

    [Fact]
    public void LatexDto_InitializesWithDefaults()
    {
        var dto = new LatexDto();
        Assert.NotNull(dto.Id);
        Assert.Equal("", dto.Latex);
        Assert.Equal(20, dto.Scale);
        Assert.Equal("#1A1A1A", dto.Color);
    }
}
