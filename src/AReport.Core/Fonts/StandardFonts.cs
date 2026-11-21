namespace AReport.Fonts;

/// <summary>Maps a <see cref="FontSpec"/> to one of the 12 standard PDF font names used by A-Report.</summary>
public static class StandardFonts
{
    public enum Family { Helvetica, Times, Courier }

    public static Family ResolveFamily(string? name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant();
        if (n.Contains("courier") || n.Contains("mono") || n.Contains("consolas")) return Family.Courier;
        if (n.Contains("times") || n.Contains("serif") && !n.Contains("sans") || n.Contains("georgia")) return Family.Times;
        return Family.Helvetica;
    }

    public static string PdfName(FontSpec font) => ResolveFamily(font.Family) switch
    {
        Family.Courier => font.Bold ? (font.Italic ? "Courier-BoldOblique" : "Courier-Bold") : (font.Italic ? "Courier-Oblique" : "Courier"),
        Family.Times => font.Bold ? (font.Italic ? "Times-BoldItalic" : "Times-Bold") : (font.Italic ? "Times-Italic" : "Times-Roman"),
        _ => font.Bold ? (font.Italic ? "Helvetica-BoldOblique" : "Helvetica-Bold") : (font.Italic ? "Helvetica-Oblique" : "Helvetica"),
    };
}
