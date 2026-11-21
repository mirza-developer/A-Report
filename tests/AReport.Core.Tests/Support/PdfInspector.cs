using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace AReport.Core.Tests.Support;

/// <summary>Reads just enough of a PDF to verify its structure. Works on uncompressed output for text checks.</summary>
internal sealed class PdfInspector
{
    private PdfInspector(byte[] bytes)
    {
        Bytes = bytes;
        Text = Encoding.Latin1.GetString(bytes); // 1 char == 1 byte, so string indexes are file offsets
    }

    public byte[] Bytes { get; }

    public string Text { get; }

    public static PdfInspector Parse(byte[] bytes) => new(bytes);

    public int PageCount => Regex.Matches(Text, @"/Type /Page(?![a-z])").Count;

    /// <summary>True when the text appears in a content stream as <c>(text) Tj</c> (uncompressed output only).</summary>
    public bool HasText(string text) => Text.Contains("(" + text + ") Tj", StringComparison.Ordinal);

    /// <summary>Asserts the header, trailer, and that every cross-reference entry points at its object.</summary>
    public void VerifyStructure()
    {
        Assert.StartsWith("%PDF-1.4\n", Text);
        Assert.EndsWith("%%EOF\n", Text);

        int startxrefIndex = Text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        Assert.True(startxrefIndex > 0, "startxref not found");
        var offsetText = Text[(startxrefIndex + "startxref\n".Length)..].Split('\n')[0];
        int xrefOffset = int.Parse(offsetText);
        Assert.StartsWith("xref\n", Text[xrefOffset..]);

        var lines = Text[xrefOffset..].Split('\n');
        var header = lines[1].Split(' ');
        Assert.Equal("0", header[0]);
        int count = int.Parse(header[1]);
        Assert.Equal("0000000000 65535 f ", lines[2]);

        for (int id = 1; id < count; id++)
        {
            var entry = lines[2 + id];
            Assert.Equal(19, entry.Length); // 20 bytes including the newline
            int offset = int.Parse(entry[..10]);
            Assert.StartsWith($"{id} 0 obj\n", Text[offset..]);
        }

        Assert.Contains($"/Size {count}", Text);
    }
}
