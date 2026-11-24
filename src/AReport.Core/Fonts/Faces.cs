namespace AReport.Fonts;

/// <summary>One concrete font used to draw text: either a built-in PDF font or an embedded TrueType font.</summary>
public interface IFace
{
    /// <summary>Unique within a document; used to share one PDF font object between runs.</summary>
    string Key { get; }

    bool IsStandard { get; }

    /// <summary>The font has no bold/italic variant, so the PDF draws it with a stroke / skew instead.</summary>
    bool SyntheticBold { get; }

    bool SyntheticItalic { get; }

    bool Covers(int codePoint);

    /// <summary>Glyph id (TrueType) or WinAnsi byte (standard font); -1 when the character cannot be drawn.</summary>
    int CodeFor(int codePoint);

    /// <summary>Advance of a code in 1/1000 em.</summary>
    double Advance1000(int code);
}

/// <summary>A built-in PDF font (Helvetica, Times, Courier families), limited to WinAnsi characters.</summary>
public sealed class StandardFace : IFace
{
    private static readonly StandardFontMetrics Metrics = new();
    private readonly FontSpec _spec;

    public StandardFace(FontSpec spec)
    {
        _spec = spec;
        PdfName = StandardFonts.PdfName(spec);
    }

    public string PdfName { get; }
    public string Key => "std:" + PdfName;
    public bool IsStandard => true;
    public bool SyntheticBold => false;
    public bool SyntheticItalic => false;

    public bool Covers(int codePoint) => codePoint <= 0xFFFF && WinAnsi.IsSupported((char)codePoint);

    public int CodeFor(int codePoint) => codePoint <= 0xFFFF && WinAnsi.TryEncode((char)codePoint, out var b) ? b : -1;

    public double Advance1000(int code) => Metrics.CharWidth1000(WinAnsi.Decode((byte)code), _spec);
}

/// <summary>A TrueType font embedded (subset) in the PDF with the Identity-H encoding: codes are glyph ids.</summary>
public sealed class TrueTypeFace : IFace
{
    public TrueTypeFace(TrueTypeFont font, string key, bool syntheticBold, bool syntheticItalic)
    {
        Font = font;
        Key = key;
        SyntheticBold = syntheticBold;
        SyntheticItalic = syntheticItalic;
    }

    public TrueTypeFont Font { get; }
    public string Key { get; }
    public bool IsStandard => false;
    public bool SyntheticBold { get; }
    public bool SyntheticItalic { get; }

    public bool Covers(int codePoint) => Font.Covers(codePoint);

    public int CodeFor(int codePoint)
    {
        var gid = Font.GlyphFor(codePoint);
        return gid == 0 ? -1 : gid;
    }

    public double Advance1000(int code) => Font.Advance1000(code);
}
