using System.Buffers.Binary;
using System.Text;

namespace AReport.Fonts;

/// <summary>Names read from a font's <c>name</c> table.</summary>
public sealed record FontNames(string Family, string Subfamily, string FullName, string PostScriptName, bool Bold, bool Italic, int Weight, bool HasGlyphOutlines)
{
    /// <summary>Family names in every language the font carries (a Chinese font is also found by its English name).</summary>
    public IReadOnlyList<string> FamilyAliases { get; init; } = [];
}

/// <summary>
/// A TrueType (glyf-outline) font: metrics, character map and glyph data. TrueType collections (.ttc) are supported.
/// CFF-based OpenType fonts are recognised (<see cref="HasGlyphOutlines"/> is false) but cannot be embedded.
/// </summary>
public sealed class TrueTypeFont
{
    private readonly byte[] _data;
    private readonly Dictionary<string, (int Offset, int Length)> _tables = new(StringComparer.Ordinal);
    private Dictionary<int, ushort>? _cmap;
    private int _glyfOffset, _locaOffset, _hmtxOffset;

    private TrueTypeFont(byte[] data, int directoryOffset)
    {
        _data = data;
        ReadDirectory(directoryOffset);
        ReadHeaderTables();
        Names = ReadNameTable(_data, _tables);
    }

    public FontNames Names { get; }
    public int UnitsPerEm { get; private set; }
    public int NumGlyphs { get; private set; }
    public int Ascender { get; private set; }
    public int Descender { get; private set; }
    public int LineGap { get; private set; }
    public int CapHeight { get; private set; }
    public double ItalicAngle { get; private set; }
    public bool IsFixedPitch { get; private set; }
    public int XMin { get; private set; }
    public int YMin { get; private set; }
    public int XMax { get; private set; }
    public int YMax { get; private set; }
    public bool HasGlyphOutlines => _tables.ContainsKey("glyf") && _tables.ContainsKey("loca");
    internal byte[] RawData => _data;

    /// <summary>Loads face <paramref name="faceIndex"/> (always 0 for plain .ttf files).</summary>
    public static TrueTypeFont Load(byte[] data, int faceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 12) throw new InvalidDataException("The font file is too small.");
        int offset = 0;
        if (Tag(data, 0) == "ttcf")
        {
            int count = (int)U32(data, 8);
            if (faceIndex < 0 || faceIndex >= count) throw new ArgumentOutOfRangeException(nameof(faceIndex));
            offset = (int)U32(data, 12 + 4 * faceIndex);
        }
        return new TrueTypeFont(data, offset);
    }

    /// <summary>Number of faces in a file (1 for .ttf, more for .ttc).</summary>
    public static int FaceCount(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && header[0] == 't' && header[1] == 't' && header[2] == 'c' && header[3] == 'f'
            ? (int)BinaryPrimitives.ReadUInt32BigEndian(header[8..])
            : 1;

    /// <summary>Reads only the names of every face of a file, without loading glyph data.</summary>
    public static IReadOnlyList<FontNames> ReadNames(string path)
    {
        var result = new List<FontNames>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var head = new byte[12];
        try { stream.ReadExactly(head); }
        catch (EndOfStreamException) { return result; }

        string tag = Tag(head, 0);
        var offsets = new List<int>();
        if (tag == "ttcf")
        {
            int count = (int)U32(head, 8);
            var list = new byte[4 * count];
            stream.ReadExactly(list);
            for (int i = 0; i < count; i++) offsets.Add((int)U32(list, 4 * i));
        }
        else if (tag is "true" or "OTTO" || U32(head, 0) == 0x00010000)
        {
            offsets.Add(0);
        }

        foreach (int dir in offsets)
        {
            try
            {
                stream.Position = dir;
                var header = new byte[12];
                stream.ReadExactly(header);
                int numTables = U16(header, 4);
                var records = new byte[16 * numTables];
                stream.ReadExactly(records);

                var tables = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
                for (int i = 0; i < numTables; i++)
                    tables[Tag(records, 16 * i)] = ((int)U32(records, 16 * i + 8), (int)U32(records, 16 * i + 12));

                if (!tables.TryGetValue("name", out var name)) continue;
                var nameData = new byte[name.Item2];
                stream.Position = name.Item1;
                stream.ReadExactly(nameData);

                // The name table is read in isolation, so rebase its offset to 0.
                var local = new Dictionary<string, (int, int)> { ["name"] = (0, nameData.Length) };
                int os2Weight = 400; bool italicSel = false, boldSel = false;
                if (tables.TryGetValue("OS/2", out var os2) && os2.Item2 >= 64)
                {
                    var os2Data = new byte[64];
                    stream.Position = os2.Item1;
                    stream.ReadExactly(os2Data);
                    os2Weight = U16(os2Data, 4);
                    int sel = U16(os2Data, 62);
                    italicSel = (sel & 1) != 0;
                    boldSel = (sel & 32) != 0;
                }
                var names = ReadNameTable(nameData, local, os2Weight, italicSel, boldSel, tables.ContainsKey("glyf") && tables.ContainsKey("loca"));
                result.Add(names);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or OverflowException)
            {
                // A damaged face is skipped; the rest of the file may still be usable.
            }
        }
        return result;
    }

    // ---------- character map and metrics ----------

    /// <summary>Glyph id for a code point, or 0 when the font has no glyph for it.</summary>
    public ushort GlyphFor(int codePoint)
    {
        _cmap ??= BuildCmap();
        return _cmap.TryGetValue(codePoint, out var gid) ? gid : (ushort)0;
    }

    public bool Covers(int codePoint) => GlyphFor(codePoint) != 0;

    /// <summary>Advance width of a glyph in font units.</summary>
    public int AdvanceUnits(int gid)
    {
        int metrics = _numberOfHMetrics;
        if (metrics == 0) return 0;
        int index = Math.Min(gid, metrics - 1);
        return U16(_data, _hmtxOffset + 4 * index);
    }

    /// <summary>Advance of a glyph in 1/1000 em.</summary>
    public double Advance1000(int gid) => AdvanceUnits(gid) * 1000.0 / UnitsPerEm;

    internal ReadOnlySpan<byte> GlyphData(int gid)
    {
        if (gid < 0 || gid >= NumGlyphs) return default;
        int start, end;
        if (_longLoca)
        {
            start = (int)U32(_data, _locaOffset + 4 * gid);
            end = (int)U32(_data, _locaOffset + 4 * (gid + 1));
        }
        else
        {
            start = U16(_data, _locaOffset + 2 * gid) * 2;
            end = U16(_data, _locaOffset + 2 * (gid + 1)) * 2;
        }
        if (end <= start) return default;
        return _data.AsSpan(_glyfOffset + start, end - start);
    }

    internal bool TryGetTable(string tag, out ReadOnlySpan<byte> table)
    {
        if (_tables.TryGetValue(tag, out var t))
        {
            table = _data.AsSpan(t.Offset, t.Length);
            return true;
        }
        table = default;
        return false;
    }

    internal int HeadIndexToLocFormat => _longLoca ? 1 : 0;

    // ---------- parsing ----------

    private int _numberOfHMetrics;
    private bool _longLoca;

    private void ReadDirectory(int offset)
    {
        int numTables = U16(_data, offset + 4);
        for (int i = 0; i < numTables; i++)
        {
            int rec = offset + 12 + 16 * i;
            int tableOffset = (int)U32(_data, rec + 8), length = (int)U32(_data, rec + 12);
            if (tableOffset < 0 || length < 0 || (long)tableOffset + length > _data.Length) continue;
            _tables[Tag(_data, rec)] = (tableOffset, length);
        }
    }

    private void ReadHeaderTables()
    {
        if (!_tables.TryGetValue("head", out var head) || !_tables.TryGetValue("maxp", out var maxp) || !_tables.TryGetValue("hhea", out var hhea))
            throw new InvalidDataException("The font is missing a required table (head, hhea or maxp).");

        UnitsPerEm = U16(_data, head.Offset + 18);
        if (UnitsPerEm == 0) UnitsPerEm = 1000;
        XMin = S16(_data, head.Offset + 36);
        YMin = S16(_data, head.Offset + 38);
        XMax = S16(_data, head.Offset + 40);
        YMax = S16(_data, head.Offset + 42);
        _longLoca = S16(_data, head.Offset + 50) != 0;
        NumGlyphs = U16(_data, maxp.Offset + 4);
        Ascender = S16(_data, hhea.Offset + 4);
        Descender = S16(_data, hhea.Offset + 6);
        LineGap = S16(_data, hhea.Offset + 8);
        _numberOfHMetrics = U16(_data, hhea.Offset + 34);

        if (_tables.TryGetValue("hmtx", out var hmtx)) _hmtxOffset = hmtx.Offset;
        if (_tables.TryGetValue("glyf", out var glyf)) _glyfOffset = glyf.Offset;
        if (_tables.TryGetValue("loca", out var loca)) _locaOffset = loca.Offset;

        CapHeight = Ascender;
        if (_tables.TryGetValue("OS/2", out var os2) && os2.Length >= 90)
        {
            int version = U16(_data, os2.Offset);
            if (version >= 2 && os2.Length >= 90) CapHeight = S16(_data, os2.Offset + 88);
        }
        if (CapHeight <= 0) CapHeight = Ascender;

        if (_tables.TryGetValue("post", out var post) && post.Length >= 16)
        {
            ItalicAngle = S32(_data, post.Offset + 4) / 65536.0;
            IsFixedPitch = U32(_data, post.Offset + 12) != 0;
        }
    }

    private static FontNames ReadNameTable(byte[] data, Dictionary<string, (int Offset, int Length)> tables, int weight = -1, bool italicSel = false, bool boldSel = false, bool? glyf = null)
    {
        string family = "", subfamily = "", full = "", ps = "", typoFamily = "", typoSub = "";
        var aliases = new List<string>();
        if (tables.TryGetValue("name", out var name) && name.Length >= 6)
        {
            int count = U16(data, name.Offset + 2), stringOffset = U16(data, name.Offset + 4);
            for (int i = 0; i < count; i++)
            {
                int rec = name.Offset + 6 + 12 * i;
                if (rec + 12 > name.Offset + name.Length) break;
                int platform = U16(data, rec), nameId = U16(data, rec + 6), length = U16(data, rec + 8), offset = U16(data, rec + 10);
                if (nameId is not (1 or 2 or 4 or 6 or 16 or 17)) continue;
                int start = name.Offset + stringOffset + offset;
                if (start < 0 || start + length > data.Length) continue;

                string value = platform is 3 or 0
                    ? Encoding.BigEndianUnicode.GetString(data, start, length)
                    : Encoding.Latin1.GetString(data, start, length);
                bool windows = platform == 3;
                int language = U16(data, rec + 4);
                bool english = platform == 3 ? (language & 0xFF) == 0x09 : platform == 1 && language == 0;
                if (nameId is 1 or 4 or 16 && value.Length > 0 && !aliases.Contains(value)) aliases.Add(value);
                switch (nameId)
                {
                    case 1 when family.Length == 0 || english: family = value; break;
                    case 2 when subfamily.Length == 0 || english: subfamily = value; break;
                    case 4 when full.Length == 0 || english: full = value; break;
                    case 6 when ps.Length == 0 || windows: ps = value; break;
                    case 16 when typoFamily.Length == 0 || english: typoFamily = value; break;
                    case 17 when typoSub.Length == 0 || english: typoSub = value; break;
                }
            }
        }

        // Names 16/17 hold the real family when 1/2 describe a style-linked group ("Foo Light" → family "Foo").
        string effectiveFamily = typoFamily.Length > 0 ? typoFamily : family;
        string effectiveSub = typoSub.Length > 0 ? typoSub : subfamily;

        string lower = (effectiveSub + " " + subfamily).ToLowerInvariant();
        bool bold = boldSel || (weight >= 600) || lower.Contains("bold") || lower.Contains("black") || lower.Contains("heavy");
        bool italic = italicSel || lower.Contains("italic") || lower.Contains("oblique");
        if (weight < 0) weight = bold ? 700 : 400;
        return new FontNames(effectiveFamily, effectiveSub, full, ps, bold, italic, weight, glyf ?? true) { FamilyAliases = aliases };
    }

    private Dictionary<int, ushort> BuildCmap()
    {
        var map = new Dictionary<int, ushort>();
        if (!_tables.TryGetValue("cmap", out var cmap) || cmap.Length < 4) return map;

        int count = U16(_data, cmap.Offset + 2);
        int best = -1, bestScore = -1;
        for (int i = 0; i < count; i++)
        {
            int rec = cmap.Offset + 4 + 8 * i;
            int platform = U16(_data, rec), encoding = U16(_data, rec + 2);
            int offset = (int)U32(_data, rec + 4);
            int format = U16(_data, cmap.Offset + offset);
            int score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 5,
                (0, 4, 12) or (0, 6, 12) => 4,
                (3, 1, 4) => 3,
                (0, 3, 4) or (0, 4, 4) => 2,
                (_, _, 12) => 1,
                (_, _, 4) => 1,
                _ => -1,
            };
            if (score > bestScore) { bestScore = score; best = cmap.Offset + offset; }
        }
        if (best < 0) return map;

        int fmt = U16(_data, best);
        if (fmt == 4) ReadFormat4(best, map);
        else if (fmt == 12) ReadFormat12(best, map);
        return map;
    }

    private void ReadFormat4(int at, Dictionary<int, ushort> map)
    {
        int segX2 = U16(_data, at + 6);
        int seg = segX2 / 2;
        int endCodes = at + 14, startCodes = endCodes + segX2 + 2, deltas = startCodes + segX2, rangeOffsets = deltas + segX2;
        for (int i = 0; i < seg; i++)
        {
            int end = U16(_data, endCodes + 2 * i), start = U16(_data, startCodes + 2 * i);
            int delta = S16(_data, deltas + 2 * i), rangeOffset = U16(_data, rangeOffsets + 2 * i);
            if (start == 0xFFFF) continue;
            for (int c = start; c <= end; c++)
            {
                int gid;
                if (rangeOffset == 0) gid = (c + delta) & 0xFFFF;
                else
                {
                    int p = rangeOffsets + 2 * i + rangeOffset + 2 * (c - start);
                    if (p + 2 > _data.Length) continue;
                    gid = U16(_data, p);
                    if (gid != 0) gid = (gid + delta) & 0xFFFF;
                }
                if (gid != 0) map[c] = (ushort)gid;
            }
        }
    }

    private void ReadFormat12(int at, Dictionary<int, ushort> map)
    {
        int groups = (int)U32(_data, at + 12);
        for (int i = 0; i < groups; i++)
        {
            int g = at + 16 + 12 * i;
            if (g + 12 > _data.Length) break;
            uint start = U32(_data, g), end = U32(_data, g + 4), gid = U32(_data, g + 8);
            if (end - start > 0x10FFFF) continue;
            for (uint c = start; c <= end; c++)
                if (gid + (c - start) is > 0 and < 0xFFFF) map[(int)c] = (ushort)(gid + (c - start));
        }
    }

    // ---------- big-endian helpers ----------

    internal static string Tag(byte[] d, int o) => Encoding.ASCII.GetString(d, o, 4);
    internal static int U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));
    internal static int S16(byte[] d, int o) => BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(o, 2));
    internal static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
    internal static int S32(byte[] d, int o) => BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(o, 4));
}
