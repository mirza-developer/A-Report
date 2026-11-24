namespace AReport.Barcodes;

/// <summary>Code 128 (code sets B and C, switching automatically to pack digit pairs).</summary>
public static class Code128Encoder
{
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    private const int StartB = 104, StartC = 105, CodeB = 100, CodeC = 99, Stop = 106;

    /// <summary>The values of the symbol characters (start, data, checksum, stop), useful for tests.</summary>
    public static IReadOnlyList<int> Encode128Values(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("A barcode needs some text.", nameof(text));
        foreach (var c in text)
            if (c is < ' ' or > '~') throw new ArgumentException($"Code 128 here supports printable ASCII only (found U+{(int)c:X4}).", nameof(text));

        var values = new List<int>();
        int i = 0;
        bool setC = StartsWithDigitPairs(text, 0);
        values.Add(setC ? StartC : StartB);

        while (i < text.Length)
        {
            if (setC)
            {
                if (i + 1 < text.Length && char.IsAsciiDigit(text[i]) && char.IsAsciiDigit(text[i + 1]))
                {
                    values.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
                    i += 2;
                    continue;
                }
                values.Add(CodeB);
                setC = false;
            }
            else if (StartsWithDigitPairs(text, i))
            {
                values.Add(CodeC);
                setC = true;
            }
            else
            {
                values.Add(text[i] - ' ');
                i++;
            }
        }

        long sum = values[0];
        for (int k = 1; k < values.Count; k++) sum += (long)values[k] * k;
        values.Add((int)(sum % 103));
        values.Add(Stop);
        return values;
    }

    /// <summary>Four or more digits in a row (or a short all-digit text) pay off in code set C.</summary>
    private static bool StartsWithDigitPairs(string text, int at)
    {
        int digits = 0;
        while (at + digits < text.Length && char.IsAsciiDigit(text[at + digits])) digits++;
        return digits >= 4 || at == 0 && digits >= 2 && digits == text.Length;
    }

    public static BarcodeMatrix Encode(string text)
    {
        var values = Encode128Values(text);
        var modules = new List<bool>();
        foreach (var value in values)
        {
            bool dark = true;
            foreach (var width in Patterns[value])
            {
                for (int k = 0; k < width - '0'; k++) modules.Add(dark);
                dark = !dark;
            }
        }

        var matrix = new BarcodeMatrix(modules.Count, 1);
        for (int c = 0; c < modules.Count; c++) matrix[c, 0] = modules[c];
        return matrix;
    }
}
