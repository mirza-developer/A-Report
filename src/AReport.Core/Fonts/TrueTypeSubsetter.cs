using System.Buffers.Binary;
using System.Text;

namespace AReport.Fonts;

/// <summary>
/// Builds a smaller TrueType file that keeps the original glyph ids but only the outlines that are used.
/// Keeping the ids means the PDF can use the identity glyph mapping without renumbering.
/// </summary>
public static class TrueTypeSubsetter
{
    private static readonly string[] CopiedTables = ["head", "hhea", "maxp", "hmtx", "cvt ", "fpgm", "prep", "OS/2"];

    public static byte[] Subset(TrueTypeFont font, IEnumerable<int> usedGlyphs)
    {
        if (!font.HasGlyphOutlines)
            throw new InvalidOperationException("The font has no TrueType outlines (CFF-based fonts cannot be embedded).");

        var keep = new SortedSet<int> { 0 };
        foreach (var gid in usedGlyphs) AddWithComponents(font, gid, keep);

        // glyf + loca (always the 32-bit loca format)
        using var glyf = new MemoryStream();
        var loca = new byte[4 * (font.NumGlyphs + 1)];
        for (int gid = 0; gid < font.NumGlyphs; gid++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(4 * gid), (uint)glyf.Length);
            if (!keep.Contains(gid)) continue;
            var data = font.GlyphData(gid);
            glyf.Write(data);
            while (glyf.Length % 4 != 0) glyf.WriteByte(0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(4 * font.NumGlyphs), (uint)glyf.Length);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var tag in CopiedTables)
            if (font.TryGetTable(tag, out var table)) tables[tag] = table.ToArray();

        var head = tables["head"];
        head[8] = head[9] = head[10] = head[11] = 0;       // checkSumAdjustment is recomputed below
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1); // indexToLocFormat = long
        tables["glyf"] = glyf.ToArray();
        tables["loca"] = loca;
        tables["post"] = MinimalPost(font);

        return Assemble(tables);
    }

    private static void AddWithComponents(TrueTypeFont font, int gid, SortedSet<int> keep)
    {
        if (gid < 0 || gid >= font.NumGlyphs || !keep.Add(gid)) return;

        var data = font.GlyphData(gid);
        if (data.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(data) >= 0) return; // simple glyph

        int pos = 10;
        while (pos + 4 <= data.Length)
        {
            int flags = BinaryPrimitives.ReadUInt16BigEndian(data[pos..]);
            int component = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 2)..]);
            AddWithComponents(font, component, keep);
            pos += 4;
            pos += (flags & 0x0001) != 0 ? 4 : 2;
            if ((flags & 0x0008) != 0) pos += 2;
            else if ((flags & 0x0040) != 0) pos += 4;
            else if ((flags & 0x0080) != 0) pos += 8;
            if ((flags & 0x0020) == 0) break;
        }
    }

    private static byte[] MinimalPost(TrueTypeFont font)
    {
        var post = new byte[32];
        if (font.TryGetTable("post", out var original) && original.Length >= 32)
            original[..32].CopyTo(post);
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000); // version 3: no glyph names
        return post;
    }

    private static byte[] Assemble(SortedDictionary<string, byte[]> tables)
    {
        int count = tables.Count;
        int searchRange = 1, entrySelector = 0;
        while (searchRange * 2 <= count) { searchRange *= 2; entrySelector++; }
        searchRange *= 16;

        using var output = new MemoryStream();
        var header = new byte[12 + 16 * count];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)(count * 16 - searchRange));

        int offset = header.Length;
        int index = 0;
        int headRecord = -1;
        var bodies = new List<byte[]>();
        foreach (var (tag, data) in tables)
        {
            int rec = 12 + 16 * index;
            Encoding.ASCII.GetBytes(tag, header.AsSpan(rec, 4));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 4), Checksum(data));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 12), (uint)data.Length);
            if (tag == "head") headRecord = offset;
            bodies.Add(data);
            offset += (data.Length + 3) & ~3;
            index++;
        }

        output.Write(header);
        foreach (var body in bodies)
        {
            output.Write(body);
            for (int pad = body.Length; pad % 4 != 0; pad++) output.WriteByte(0);
        }

        var file = output.ToArray();
        uint adjustment = 0xB1B0AFBA - Checksum(file);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headRecord + 8), adjustment);
        return file;
    }

    private static uint Checksum(byte[] data)
    {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 4)
        {
            uint word = 0;
            for (int k = 0; k < 4; k++)
                word = (word << 8) | (i + k < data.Length ? data[i + k] : (uint)0);
            sum += word;
        }
        return sum;
    }
}
