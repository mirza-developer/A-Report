using AReport.Fonts;
using AReport.Model;

namespace AReport.Layout;

public sealed record TextBlock(IReadOnlyList<TextLine> Lines, double ContentHeightMm);

/// <summary>Word wrapping, alignment and vertical placement of text inside a box. The only place that does this.</summary>
public sealed class TextLayouter(IFontMetrics metrics)
{
    public const double DefaultPaddingMm = 1;

    public TextBlock Arrange(
        string text, FontSpec font,
        double xMm, double yMm, double widthMm, double heightMm,
        HAlign horizontal, VAlign vertical, bool wrap,
        double paddingMm = DefaultPaddingMm)
    {
        double innerWidthPt = Units.MmToPt(Math.Max(0, widthMm - 2 * paddingMm));
        var paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            if (wrap) lines.AddRange(Wrap(paragraph, font, innerWidthPt));
            else lines.Add(paragraph);
        }

        double lineHeightMm = Units.PtToMm(metrics.LineHeightPt(font));
        double contentHeight = lines.Count * lineHeightMm;
        double top = vertical switch
        {
            VAlign.Middle => yMm + (heightMm - contentHeight) / 2,
            VAlign.Bottom => yMm + heightMm - paddingMm - contentHeight,
            _ => yMm + paddingMm,
        };
        double ascentMm = Units.PtToMm(font.SizePt * 0.9);

        var result = new List<TextLine>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            double lineWidthMm = Units.PtToMm(metrics.MeasureWidthPt(lines[i], font));
            double x = horizontal switch
            {
                HAlign.Center => xMm + (widthMm - lineWidthMm) / 2,
                HAlign.Right => xMm + widthMm - paddingMm - lineWidthMm,
                _ => xMm + paddingMm,
            };
            result.Add(new TextLine(lines[i], x, top + i * lineHeightMm + ascentMm));
        }
        return new TextBlock(result, contentHeight);
    }

    private IEnumerable<string> Wrap(string paragraph, FontSpec font, double maxWidthPt)
    {
        if (paragraph.Length == 0)
        {
            yield return "";
            yield break;
        }

        string current = "";
        foreach (var word in paragraph.Split(' '))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (metrics.MeasureWidthPt(candidate, font) <= maxWidthPt)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                yield return current;
                current = "";
            }

            // The word alone may still be too wide: break it by characters.
            string rest = word;
            while (rest.Length > 0 && metrics.MeasureWidthPt(rest, font) > maxWidthPt)
            {
                int fit = 1;
                while (fit < rest.Length && metrics.MeasureWidthPt(rest[..(fit + 1)], font) <= maxWidthPt)
                    fit++;
                yield return rest[..fit];
                rest = rest[fit..];
            }
            current = rest;
        }

        yield return current;
    }
}
