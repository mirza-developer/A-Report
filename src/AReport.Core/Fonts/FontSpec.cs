namespace AReport.Fonts;

public sealed record FontSpec(string Family, double SizePt, bool Bold, bool Italic);

/// <summary>Measures text. Kept behind an interface so a TrueType-based implementation can be added later.</summary>
public interface IFontMetrics
{
    double MeasureWidthPt(string text, FontSpec font);

    double LineHeightPt(FontSpec font);
}
