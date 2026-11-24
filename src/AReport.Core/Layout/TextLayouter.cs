using AReport.Fonts;
using AReport.Model;

namespace AReport.Layout;

public sealed record TextBlock(IReadOnlyList<TextLine> Lines, double ContentHeightMm);

/// <summary>Word wrapping, bidi, alignment and vertical placement of text inside a box. The only place that does this.</summary>
public sealed class TextLayouter
{
    public const double DefaultPaddingMm = 1;

    private readonly FontSystem _fonts;
    private readonly TextShaper _shaper;

    public TextLayouter(FontSystem fonts)
    {
        _fonts = fonts;
        _shaper = new TextShaper(fonts);
    }

    /// <summary>Latin-only layout with the built-in PDF fonts (kept for callers that only have font metrics).</summary>
    public TextLayouter(IFontMetrics metrics) : this(FontSystem.StandardOnly)
    {
    }

    public FontSystem Fonts => _fonts;

    public TextBlock Arrange(
        string text, FontSpec font,
        double xMm, double yMm, double widthMm, double heightMm,
        HAlign horizontal, VAlign vertical, bool wrap,
        double paddingMm = DefaultPaddingMm,
        TextDirection direction = TextDirection.Auto,
        DigitStyle digits = DigitStyle.Latin,
        Action<string>? warn = null)
    {
        double innerWidthPt = wrap ? Units.MmToPt(Math.Max(0, widthMm - 2 * paddingMm)) : double.PositiveInfinity;
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var shaped = new List<ShapedLine>();
        foreach (var paragraph in paragraphs)
            shaped.AddRange(_shaper.Layout(paragraph, font, direction, digits, innerWidthPt, warn));

        double lineHeightMm = Units.PtToMm(font.SizePt * 1.2);
        double contentHeight = shaped.Count * lineHeightMm;
        double top = vertical switch
        {
            VAlign.Middle => yMm + (heightMm - contentHeight) / 2,
            VAlign.Bottom => yMm + heightMm - paddingMm - contentHeight,
            _ => yMm + paddingMm,
        };
        double ascentMm = Units.PtToMm(font.SizePt * 0.9);

        var result = new List<TextLine>(shaped.Count);
        for (int i = 0; i < shaped.Count; i++)
        {
            double lineWidthMm = Units.PtToMm(shaped[i].WidthPt);
            double x = horizontal switch
            {
                HAlign.Center => xMm + (widthMm - lineWidthMm) / 2,
                HAlign.Right => xMm + widthMm - paddingMm - lineWidthMm,
                _ => xMm + paddingMm,
            };
            result.Add(new TextLine(shaped[i].Text, x, top + i * lineHeightMm + ascentMm, shaped[i].Runs, lineWidthMm));
        }
        return new TextBlock(result, contentHeight);
    }
}
