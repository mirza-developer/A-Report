using System.Text;
using AReport.Fonts;

namespace AReport.Pdf;

/// <summary>
/// Collects the fonts a document uses and writes them at the end: built-in fonts as Type1 references,
/// TrueType fonts as subsetted CID fonts (Identity-H) with a ToUnicode map so the text stays searchable.
/// </summary>
internal sealed class PdfFontResources(PdfFile file, bool compress)
{
    private sealed class Entry(string resource, IFace face)
    {
        public string Resource { get; } = resource;
        public IFace Face { get; } = face;
        public SortedDictionary<int, string> Glyphs { get; } = [];
        public int ObjectId { get; set; }
    }

    private readonly Dictionary<string, Entry> _fonts = [];

    /// <summary>The resource name (F1, F2…) of a face; records the glyphs the run uses.</summary>
    public string Use(GlyphRun run)
    {
        var entry = Register(run.Face);
        for (int i = 0; i < run.Codes.Count; i++)
        {
            if (!entry.Glyphs.ContainsKey(run.Codes[i]))
                entry.Glyphs[run.Codes[i]] = run.Sources is { } sources && i < sources.Count ? sources[i] : "";
        }
        return entry.Resource;
    }

    public string Use(IFace face) => Register(face).Resource;

    private Entry Register(IFace face)
    {
        if (!_fonts.TryGetValue(face.Key, out var entry))
        {
            entry = new Entry($"F{_fonts.Count + 1}", face);
            _fonts[face.Key] = entry;
        }
        return entry;
    }

    public bool IsEmpty => _fonts.Count == 0;

    /// <summary>Writes every font object. Call once, after all page content has been produced.</summary>
    public void Write()
    {
        foreach (var entry in _fonts.Values)
        {
            entry.ObjectId = entry.Face is TrueTypeFace tt
                ? WriteTrueType(entry, tt)
                : file.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /{((StandardFace)entry.Face).PdfName} /Encoding /WinAnsiEncoding >>");
        }
    }

    public string ResourceDictionary() =>
        "/Font << " + string.Join(' ', _fonts.Values.Select(e => $"/{e.Resource} {e.ObjectId} 0 R")) + " >>";

    private int WriteTrueType(Entry entry, TrueTypeFace face)
    {
        var font = face.Font;
        var subset = TrueTypeSubsetter.Subset(font, entry.Glyphs.Keys);
        string baseName = Tag(entry) + "+" + Sanitize(font.Names.PostScriptName.Length > 0 ? font.Names.PostScriptName : font.Names.Family);

        int fontFile = file.Add(PdfSyntax.Stream($"/Length1 {subset.Length}", subset, compress: true));

        double scale = 1000.0 / font.UnitsPerEm;
        int descriptor = file.Add(
            $"<< /Type /FontDescriptor /FontName /{baseName} /Flags 4 " +
            $"/FontBBox [{Math.Round(font.XMin * scale)} {Math.Round(font.YMin * scale)} {Math.Round(font.XMax * scale)} {Math.Round(font.YMax * scale)}] " +
            $"/ItalicAngle {PdfSyntax.Num(font.ItalicAngle)} /Ascent {Math.Round(font.Ascender * scale)} /Descent {Math.Round(font.Descender * scale)} " +
            $"/CapHeight {Math.Round(font.CapHeight * scale)} /StemV 80 /FontFile2 {fontFile} 0 R >>");

        var widths = new StringBuilder("[");
        foreach (var gid in entry.Glyphs.Keys)
            widths.Append(' ').Append(gid).Append(" [").Append(PdfSyntax.Num(Math.Round(font.Advance1000(gid), 1))).Append(']');
        widths.Append(" ]");

        int cidFont = file.Add(
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{baseName} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {descriptor} 0 R /DW 1000 /W {widths} /CIDToGIDMap /Identity >>");

        int toUnicode = file.Add(PdfSyntax.Stream("", Encoding.ASCII.GetBytes(BuildToUnicode(entry)), compress));

        return file.Add(
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{baseName} /Encoding /Identity-H /DescendantFonts [{cidFont} 0 R] /ToUnicode {toUnicode} 0 R >>");
    }

    private static string BuildToUnicode(Entry entry)
    {
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");

        var mapped = entry.Glyphs.Where(g => g.Value.Length > 0).ToList();
        for (int i = 0; i < mapped.Count; i += 100)
        {
            var chunk = mapped.Skip(i).Take(100).ToList();
            sb.Append(chunk.Count).Append(" beginbfchar\n");
            foreach (var (gid, text) in chunk)
                sb.Append('<').Append(gid.ToString("X4")).Append("> <").Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text))).Append(">\n");
            sb.Append("endbfchar\n");
        }
        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend");
        return sb.ToString();
    }

    private static string Tag(Entry entry)
    {
        // Six capital letters derived from the font and the glyph set, as the PDF spec asks for subset fonts.
        int hash = entry.Face.Key.GetHashCode(StringComparison.Ordinal);
        foreach (var gid in entry.Glyphs.Keys) hash = unchecked(hash * 31 + gid);
        var chars = new char[6];
        uint h = unchecked((uint)hash);
        for (int i = 0; i < 6; i++) { chars[i] = (char)('A' + h % 26); h /= 26; }
        return new string(chars);
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name) if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-') sb.Append(c);
        return sb.Length == 0 ? "Font" : sb.ToString();
    }
}
