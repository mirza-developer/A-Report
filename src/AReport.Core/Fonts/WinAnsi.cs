namespace AReport.Fonts;

/// <summary>Windows-1252 ("WinAnsi") encoding, the encoding of the 14 standard PDF fonts.</summary>
public static class WinAnsi
{
    private static readonly Dictionary<char, byte> Specials = new()
    {
        ['\u20AC'] = 0x80, ['\u201A'] = 0x82, ['\u0192'] = 0x83, ['\u201E'] = 0x84, ['\u2026'] = 0x85,
        ['\u2020'] = 0x86, ['\u2021'] = 0x87, ['\u02C6'] = 0x88, ['\u2030'] = 0x89, ['\u0160'] = 0x8A,
        ['\u2039'] = 0x8B, ['\u0152'] = 0x8C, ['\u017D'] = 0x8E, ['\u2018'] = 0x91, ['\u2019'] = 0x92,
        ['\u201C'] = 0x93, ['\u201D'] = 0x94, ['\u2022'] = 0x95, ['\u2013'] = 0x96, ['\u2014'] = 0x97,
        ['\u02DC'] = 0x98, ['\u2122'] = 0x99, ['\u0161'] = 0x9A, ['\u203A'] = 0x9B, ['\u0153'] = 0x9C,
        ['\u017E'] = 0x9E, ['\u0178'] = 0x9F,
    };

    public static bool TryEncode(char c, out byte value)
    {
        if (c is >= ' ' and <= '~' || c is >= '\u00A0' and <= '\u00FF')
        {
            value = (byte)c;
            return true;
        }
        if (c == '\t')
        {
            value = (byte)' ';
            return true;
        }
        return Specials.TryGetValue(c, out value);
    }

    public static bool IsSupported(char c) => TryEncode(c, out _);

    public static bool IsFullySupported(string text) => text.All(IsSupported);

    /// <summary>Unsupported characters become '?'.</summary>
    public static byte[] Encode(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++)
            bytes[i] = TryEncode(text[i], out var b) ? b : (byte)'?';
        return bytes;
    }
}
