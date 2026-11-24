using System.Text;

namespace AReport.Barcodes;

public enum QrEccLevel { Low, Medium, Quartile, High }

/// <summary>QR Code (ISO 18004) encoder: numeric, alphanumeric and UTF-8 byte modes, versions 1-40, automatic mask choice.</summary>
public static class QrEncoder
{
    private const string AlphanumericChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private static readonly int[][] EccPerBlock =
    [
        [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    ];

    private static readonly int[][] BlockCount =
    [
        [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
    ];

    private static readonly int[] FormatBits = [1, 0, 3, 2];

    /// <summary>Encodes text. When <paramref name="boostEcc"/> is set the error correction is raised as far as the chosen version allows.</summary>
    public static BarcodeMatrix Encode(string text, QrEccLevel ecc = QrEccLevel.Medium, bool boostEcc = true)
    {
        var codewords = BuildCodewords(text, ref ecc, boostEcc, out int version);
        return Build(version, ecc, codewords);
    }

    /// <summary>Data codewords followed by error-correction codewords (interleaved), and the chosen version. Exposed for tests.</summary>
    internal static byte[] BuildCodewords(string text, ref QrEccLevel ecc, bool boostEcc, out int version)
    {
        ArgumentNullException.ThrowIfNull(text);

        var (mode, bytes, charCount) = Analyse(text);

        version = 1;
        int dataBits;
        while (true)
        {
            dataBits = 4 + CountBits(mode, version) + PayloadBits(mode, charCount, bytes.Length);
            if (dataBits <= DataCodewords(version, (int)ecc) * 8) break;
            if (++version > 40) throw new ArgumentException("The text is too long for a QR code.", nameof(text));
        }

        if (boostEcc)
        {
            foreach (var higher in new[] { QrEccLevel.Medium, QrEccLevel.Quartile, QrEccLevel.High })
                if (higher > ecc && dataBits <= DataCodewords(version, (int)higher) * 8) ecc = higher;
        }

        var bits = new List<bool>();
        void Append(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }

        Append(mode switch { Mode.Numeric => 1, Mode.Alphanumeric => 2, _ => 4 }, 4);
        Append(mode == Mode.Byte ? bytes.Length : charCount, CountBits(mode, version));
        WritePayload(mode, text, bytes, Append);

        int capacityBits = DataCodewords(version, (int)ecc) * 8;
        Append(0, Math.Min(4, capacityBits - bits.Count));
        Append(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacityBits; pad ^= 0xEC ^ 0x11) Append(pad, 8);

        var data = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i]) data[i >> 3] |= (byte)(0x80 >> (i & 7));

        return AddEccAndInterleave(data, version, ecc);
    }

    // ---------- mode selection and payload ----------

    private enum Mode { Numeric, Alphanumeric, Byte }

    private static (Mode, byte[], int) Analyse(string text)
    {
        if (text.Length > 0 && text.All(c => c is >= '0' and <= '9')) return (Mode.Numeric, [], text.Length);
        if (text.Length > 0 && text.All(c => AlphanumericChars.Contains(c))) return (Mode.Alphanumeric, [], text.Length);
        var bytes = Encoding.UTF8.GetBytes(text);
        return (Mode.Byte, bytes, bytes.Length);
    }

    private static int CountBits(Mode mode, int version)
    {
        int tier = version <= 9 ? 0 : version <= 26 ? 1 : 2;
        return mode switch
        {
            Mode.Numeric => new[] { 10, 12, 14 }[tier],
            Mode.Alphanumeric => new[] { 9, 11, 13 }[tier],
            _ => new[] { 8, 16, 16 }[tier],
        };
    }

    private static int PayloadBits(Mode mode, int charCount, int byteCount) => mode switch
    {
        Mode.Numeric => charCount / 3 * 10 + (charCount % 3 == 0 ? 0 : charCount % 3 * 3 + 1),
        Mode.Alphanumeric => charCount / 2 * 11 + charCount % 2 * 6,
        _ => byteCount * 8,
    };

    private static void WritePayload(Mode mode, string text, byte[] bytes, Action<int, int> append)
    {
        switch (mode)
        {
            case Mode.Numeric:
                for (int i = 0; i < text.Length;)
                {
                    int n = Math.Min(3, text.Length - i);
                    append(int.Parse(text.AsSpan(i, n)), n * 3 + 1);
                    i += n;
                }
                break;
            case Mode.Alphanumeric:
                for (int i = 0; i + 1 < text.Length; i += 2)
                    append(AlphanumericChars.IndexOf(text[i]) * 45 + AlphanumericChars.IndexOf(text[i + 1]), 11);
                if (text.Length % 2 == 1) append(AlphanumericChars.IndexOf(text[^1]), 6);
                break;
            default:
                foreach (var b in bytes) append(b, 8);
                break;
        }
    }

    // ---------- capacity ----------

    private static int RawDataModules(int version)
    {
        int result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            int align = version / 7 + 2;
            result -= (25 * align - 10) * align - 55;
            if (version >= 7) result -= 36;
        }
        return result;
    }

    private static int DataCodewords(int version, int ecc) =>
        RawDataModules(version) / 8 - EccPerBlock[ecc][version] * BlockCount[ecc][version];

    // ---------- Reed-Solomon ----------

    private static int Multiply(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z;
    }

    private static byte[] Divisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = (byte)Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 2);
        }
        return result;
    }

    private static byte[] Remainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            int factor = b ^ result[0];
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (int i = 0; i < divisor.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
        }
        return result;
    }

    private static byte[] AddEccAndInterleave(byte[] data, int version, QrEccLevel ecc)
    {
        int level = (int)ecc;
        int blocks = BlockCount[level][version], eccLen = EccPerBlock[level][version];
        int rawCodewords = RawDataModules(version) / 8;
        int shortBlocks = blocks - rawCodewords % blocks;
        int shortLen = rawCodewords / blocks;

        var divisor = Divisor(eccLen);
        var parts = new List<byte[]>();
        for (int i = 0, k = 0; i < blocks; i++)
        {
            int length = shortLen - eccLen + (i < shortBlocks ? 0 : 1);
            var block = data.AsSpan(k, length).ToArray();
            k += length;
            var e = Remainder(block, divisor);
            var full = new byte[shortLen + 1];
            Array.Copy(block, full, block.Length);
            Array.Copy(e, 0, full, shortLen + 1 - eccLen, eccLen);
            parts.Add(full);
        }

        var result = new List<byte>();
        for (int i = 0; i < shortLen + 1; i++)
            for (int j = 0; j < blocks; j++)
                if (i != shortLen - eccLen || j >= shortBlocks) result.Add(parts[j][i]);
        return result.ToArray();
    }

    // ---------- symbol construction ----------

    private static BarcodeMatrix Build(int version, QrEccLevel ecc, byte[] codewords)
    {
        int size = version * 4 + 17;
        var modules = new bool[size, size];
        var isFunction = new bool[size, size];

        void Set(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        for (int i = 0; i < size; i++)
        {
            Set(6, i, i % 2 == 0);
            Set(i, 6, i % 2 == 0);
        }
        Finder(3, 3); Finder(size - 4, 3); Finder(3, size - 4);

        void Finder(int cx, int cy)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(x, y, dist != 2 && dist != 4);
                }
        }

        var positions = AlignmentPositions(version, size);
        for (int i = 0; i < positions.Length; i++)
            for (int j = 0; j < positions.Length; j++)
            {
                if (i == 0 && j == 0 || i == 0 && j == positions.Length - 1 || i == positions.Length - 1 && j == 0) continue;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }

        DrawFormat(0);
        DrawVersion();

        void DrawFormat(int mask)
        {
            int data = (FormatBits[(int)ecc] << 3) | mask;
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;

            for (int i = 0; i <= 5; i++) Set(8, i, Bit(i));
            Set(8, 7, Bit(6)); Set(8, 8, Bit(7)); Set(7, 8, Bit(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));
            for (int i = 0; i < 8; i++) Set(size - 1 - i, 8, Bit(i));
            for (int i = 8; i < 15; i++) Set(8, size - 15 + i, Bit(i));
            Set(8, size - 8, true);
        }

        void DrawVersion()
        {
            if (version < 7) return;
            int rem = version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(a, b, bit);
                Set(b, a, bit);
            }
        }

        // data codewords, zig-zag from the bottom-right corner
        int bitIndex = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (isFunction[y, x] || bitIndex >= codewords.Length * 8) continue;
                    modules[y, x] = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                    bitIndex++;
                }
        }

        // choose the mask with the lowest penalty
        int bestMask = 0;
        long bestPenalty = long.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, mask);
            DrawFormat(mask);
            long penalty = Penalty(modules, size);
            if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
            ApplyMask(modules, isFunction, mask); // undo
        }
        ApplyMask(modules, isFunction, bestMask);
        DrawFormat(bestMask);

        var matrix = new BarcodeMatrix(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++) matrix[x, y] = modules[y, x];
        return matrix;
    }

    private static int[] AlignmentPositions(int version, int size)
    {
        if (version == 1) return [];
        int count = version / 7 + 2;
        int step = version == 32 ? 26 : (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, pos = size - 7; i >= 1; i--, pos -= step) result[i] = pos;
        return result;
    }

    private static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask)
    {
        int size = modules.GetLength(0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert && !isFunction[y, x]) modules[y, x] = !modules[y, x];
            }
    }

    private static long Penalty(bool[,] m, int size)
    {
        long penalty = 0;
        int dark = 0;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int a = 0; a < size; a++)
            {
                int run = 1;
                var history = new StringBuilder();
                for (int b = 0; b < size; b++)
                {
                    bool v = pass == 0 ? m[a, b] : m[b, a];
                    history.Append(v ? '1' : '0');
                    if (b > 0)
                    {
                        bool prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                        if (v == prev) { run++; if (run == 5) penalty += 3; else if (run > 5) penalty++; }
                        else run = 1;
                    }
                }
                var line = history.ToString();
                foreach (var pattern in new[] { "10111010000", "00001011101" })
                    for (int i = line.IndexOf(pattern, StringComparison.Ordinal); i >= 0; i = line.IndexOf(pattern, i + 1, StringComparison.Ordinal))
                        penalty += 40;
            }
        }

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (m[y, x]) dark++;
                if (x + 1 < size && y + 1 < size && m[y, x] == m[y, x + 1] && m[y, x] == m[y + 1, x] && m[y, x] == m[y + 1, x + 1]) penalty += 3;
            }

        int total = size * size;
        int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return penalty + Math.Max(0, k) * 10;
    }
}
