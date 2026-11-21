namespace AReport.Fonts;

/// <summary>
/// Character widths of the standard PDF fonts (units of 1/1000 em, characters 32..126).
/// Helvetica regular/oblique and bold use their AFM widths, Times uses the Times-Roman widths for all styles
/// (a close approximation for bold/italic) and Courier is monospaced.
/// </summary>
public sealed class StandardFontMetrics : IFontMetrics
{
    private const int First = 32;
    private const int DefaultNonAscii = 556;

    private static readonly int[] Helvetica =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,         // space..'/'
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556,                                         // 0..9
        278, 278, 584, 584, 584, 556, 1015,                                                       // : ; < = > ? @
        667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, // A..Z
        278, 278, 278, 469, 556, 333,                                                             // [ \ ] ^ _ `
        556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, // a..z
        334, 260, 334, 584,                                                                       // { | } ~
    ];

    private static readonly int[] HelveticaBold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556,
        333, 333, 584, 584, 584, 611, 975,
        722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611,
        333, 278, 333, 584, 556, 333,
        556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500,
        389, 280, 389, 584,
    ];

    private static readonly int[] Times =
    [
        250, 333, 408, 500, 500, 833, 778, 180, 333, 333, 500, 564, 250, 333, 250, 278,
        500, 500, 500, 500, 500, 500, 500, 500, 500, 500,
        278, 278, 564, 564, 564, 444, 921,
        722, 667, 667, 722, 611, 556, 722, 722, 333, 389, 722, 611, 889, 722, 722, 556, 722, 667, 556, 611, 722, 722, 944, 722, 722, 611,
        333, 278, 333, 469, 500, 333,
        444, 500, 444, 500, 444, 333, 500, 500, 278, 278, 500, 278, 778, 500, 500, 500, 500, 333, 389, 278, 500, 500, 722, 500, 500, 444,
        480, 200, 480, 541,
    ];

    public double MeasureWidthPt(string text, FontSpec font)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);

        var family = StandardFonts.ResolveFamily(font.Family);
        long units = 0;
        foreach (char c in text)
            units += CharWidth(c, family, font.Bold);
        return units * font.SizePt / 1000.0;
    }

    public double LineHeightPt(FontSpec font) => font.SizePt * 1.2;

    private static int CharWidth(char c, StandardFonts.Family family, bool bold)
    {
        if (family == StandardFonts.Family.Courier) return 600;

        int index = c - First;
        bool ascii = c >= First && c <= '~';
        return family switch
        {
            StandardFonts.Family.Times => ascii ? Times[index] : 500,
            _ => ascii ? (bold ? HelveticaBold[index] : Helvetica[index]) : DefaultNonAscii,
        };
    }
}
