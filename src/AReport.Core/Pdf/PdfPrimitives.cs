using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AReport.Pdf;

/// <summary>Low-level PDF syntax helpers.</summary>
internal static class PdfSyntax
{
    public static string Num(double value)
    {
        string s = Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }

    public static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    /// <summary>A PDF text string: literal when plain ASCII, otherwise UTF-16BE hex with a BOM.</summary>
    public static string TextString(string text)
    {
        if (text.All(c => c is >= ' ' and <= '~'))
            return "(" + text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";

        var bytes = Encoding.BigEndianUnicode.GetBytes(text);
        return "<FEFF" + Convert.ToHexString(bytes) + ">";
    }

    public static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }

    /// <summary>A stream object whose dictionary entries already describe the data (no extra compression).</summary>
    public static byte[] RawStream(string dictionaryEntries, byte[] data)
    {
        using var ms = new MemoryStream();
        ms.Write(Ascii($"<< {dictionaryEntries} /Length {data.Length} >>\nstream\n"));
        ms.Write(data);
        ms.Write(Ascii("\nendstream"));
        return ms.ToArray();
    }

    /// <summary>A stream object, optionally Flate-compressed.</summary>
    public static byte[] Stream(string dictionaryEntries, byte[] data, bool compress)
        => compress
            ? RawStream(dictionaryEntries + " /Filter /FlateDecode", Deflate(data))
            : RawStream(dictionaryEntries, data);
}

/// <summary>Collects numbered objects and serialises them with a cross-reference table.</summary>
internal sealed class PdfFile
{
    private readonly List<byte[]?> _objects = [];

    public int Reserve()
    {
        _objects.Add(null);
        return _objects.Count;
    }

    public void Set(int id, byte[] body) => _objects[id - 1] = body;

    public int Add(byte[] body)
    {
        int id = Reserve();
        Set(id, body);
        return id;
    }

    public int Add(string body) => Add(PdfSyntax.Ascii(body));

    public byte[] Build(int rootId, int infoId)
    {
        using var ms = new MemoryStream();
        ms.Write(PdfSyntax.Ascii("%PDF-1.4\n"));
        ms.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        var offsets = new long[_objects.Count];
        for (int i = 0; i < _objects.Count; i++)
        {
            offsets[i] = ms.Position;
            ms.Write(PdfSyntax.Ascii($"{i + 1} 0 obj\n"));
            ms.Write(_objects[i] ?? throw new InvalidOperationException($"PDF object {i + 1} was reserved but never written."));
            ms.Write(PdfSyntax.Ascii("\nendobj\n"));
        }

        long xref = ms.Position;
        var sb = new StringBuilder();
        sb.Append("xref\n0 ").Append(_objects.Count + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        sb.Append("trailer\n<< /Size ").Append(_objects.Count + 1)
          .Append(" /Root ").Append(rootId).Append(" 0 R /Info ").Append(infoId).Append(" 0 R >>\n");
        sb.Append("startxref\n").Append(xref).Append("\n%%EOF\n");
        ms.Write(PdfSyntax.Ascii(sb.ToString()));
        return ms.ToArray();
    }
}
