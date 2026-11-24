using System.Text;

namespace AReport.Barcodes;

/// <summary>Data Matrix ECC 200 (ISO 16022), square symbols 10x10 to 144x144, ASCII encodation with digit-pair packing.</summary>
public static class DataMatrixEncoder
{
    // size, data region size, regions per side, data codewords, error codewords, blocks
    private static readonly (int Size, int Region, int Regions, int Data, int Ecc, int Blocks)[] Symbols =
    [
        (10, 8, 1, 3, 5, 1), (12, 10, 1, 5, 7, 1), (14, 12, 1, 8, 10, 1), (16, 14, 1, 12, 12, 1), (18, 16, 1, 18, 14, 1),
        (20, 18, 1, 22, 18, 1), (22, 20, 1, 30, 20, 1), (24, 22, 1, 36, 24, 1), (26, 24, 1, 44, 28, 1), (32, 14, 2, 62, 36, 1),
        (36, 16, 2, 86, 42, 1), (40, 18, 2, 114, 48, 1), (44, 20, 2, 144, 56, 1), (48, 22, 2, 174, 68, 1), (52, 24, 2, 204, 84, 2),
        (64, 14, 4, 280, 112, 2), (72, 16, 4, 368, 144, 4), (80, 18, 4, 456, 192, 4), (88, 20, 4, 576, 224, 4), (96, 22, 4, 696, 272, 4),
        (104, 24, 4, 816, 336, 6), (120, 18, 6, 1050, 408, 6), (132, 20, 6, 1304, 496, 8), (144, 22, 6, 1558, 620, 10),
    ];

    /// <summary>Encodes text (non-ASCII characters are written as UTF-8 bytes).</summary>
    public static BarcodeMatrix Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) throw new ArgumentException("A barcode needs some text.", nameof(text));

        var symbol = Choose(text, out var codewords);
        int mapping = symbol.Region * symbol.Regions;
        var placement = new Placement(mapping, mapping, codewords);
        placement.Place();

        var result = new BarcodeMatrix(symbol.Size, symbol.Size);
        int matrixY = 0;
        for (int y = 0; y < mapping; y++)
        {
            if (y % symbol.Region == 0)
            {
                for (int x = 0; x < symbol.Size; x++) result[x, matrixY] = x % 2 == 0;
                matrixY++;
            }

            int matrixX = 0;
            for (int x = 0; x < mapping; x++)
            {
                if (x % symbol.Region == 0)
                {
                    result[matrixX, matrixY] = true;
                    matrixX++;
                }
                result[matrixX, matrixY] = placement.GetBit(x, y);
                matrixX++;
                if (x % symbol.Region == symbol.Region - 1)
                {
                    result[matrixX, matrixY] = y % 2 == 0;
                    matrixX++;
                }
            }
            matrixY++;

            if (y % symbol.Region == symbol.Region - 1)
            {
                for (int x = 0; x < symbol.Size; x++) result[x, matrixY] = true;
                matrixY++;
            }
        }
        return result;
    }

    /// <summary>Data and error-correction codewords of the smallest fitting symbol. Exposed for tests.</summary>
    internal static int[] BuildCodewords(string text)
    {
        Choose(text, out var codewords);
        return codewords;
    }

    private static (int Size, int Region, int Regions, int Data, int Ecc, int Blocks) Choose(string text, out int[] codewords)
    {
        var data = EncodeAscii(Encoding.UTF8.GetBytes(text));
        var symbol = Symbols.FirstOrDefault(s => s.Data >= data.Count);
        if (symbol.Size == 0) throw new ArgumentException("The text is too long for a Data Matrix symbol.", nameof(text));

        // pad to capacity
        if (data.Count < symbol.Data) data.Add(129);
        while (data.Count < symbol.Data)
        {
            int position = data.Count + 1;
            int value = 129 + (149 * position % 253 + 1);
            if (value > 254) value -= 254;
            data.Add(value);
        }

        codewords = AddEcc(data, symbol.Data, symbol.Ecc, symbol.Blocks);
        return symbol;
    }

    private static List<int> EncodeAscii(byte[] bytes)
    {
        var codewords = new List<int>();
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (char.IsAsciiDigit((char)b) && i + 1 < bytes.Length && char.IsAsciiDigit((char)bytes[i + 1]))
            {
                codewords.Add(130 + (b - '0') * 10 + (bytes[i + 1] - '0'));
                i++;
            }
            else if (b < 128) codewords.Add(b + 1);
            else
            {
                codewords.Add(235);          // upper shift
                codewords.Add(b - 127);
            }
        }
        return codewords;
    }

    // ---------- Reed-Solomon over GF(256), primitive polynomial 0x12D ----------

    private static readonly int[] Exp = new int[512];
    private static readonly int[] Log = new int[256];

    static DataMatrixEncoder()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = x;
            Log[x] = i;
            x <<= 1;
            if (x >= 256) x ^= 0x12D;
        }
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    private static int Mul(int a, int b) => a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]];

    private static int[] Generator(int degree)
    {
        var g = new int[degree + 1];
        g[0] = 1;
        for (int i = 1; i <= degree; i++)
        {
            for (int j = i; j >= 1; j--) g[j] = g[j - 1] ^ Mul(g[j], Exp[i]);
            g[0] = Mul(g[0], Exp[i]);
        }
        return g; // g[degree] == 1 (highest coefficient), g[0] is the constant term
    }

    private static int[] Ecc(int[] block, int eccCount)
    {
        var g = Generator(eccCount);
        var remainder = new int[eccCount];
        foreach (var d in block)
        {
            int factor = d ^ remainder[eccCount - 1];
            for (int i = eccCount - 1; i > 0; i--) remainder[i] = remainder[i - 1] ^ Mul(factor, g[i]);
            remainder[0] = Mul(factor, g[0]);
        }
        var result = new int[eccCount];
        for (int i = 0; i < eccCount; i++) result[i] = remainder[eccCount - 1 - i];
        return result;
    }

    private static int[] AddEcc(List<int> data, int dataCount, int eccCount, int blocks)
    {
        var all = new int[dataCount + eccCount];
        for (int i = 0; i < dataCount; i++) all[i] = data[i];

        int perBlock = eccCount / blocks;
        for (int b = 0; b < blocks; b++)
        {
            var block = new List<int>();
            for (int d = b; d < dataCount; d += blocks) block.Add(data[d]);
            var ecc = Ecc(block.ToArray(), perBlock);
            for (int e = 0, pos = 0; b + e * blocks < eccCount; e++, pos++)
                all[dataCount + b + e * blocks] = ecc[pos];
        }
        return all;
    }

    // ---------- module placement (ISO 16022 Annex F) ----------

    private sealed class Placement(int rows, int cols, int[] codewords)
    {
        private readonly int[] _bits = Enumerable.Repeat(-1, rows * cols).ToArray();

        public bool GetBit(int col, int row) => _bits[row * cols + col] == 1;

        private bool HasBit(int col, int row) => _bits[row * cols + col] >= 0;

        private void SetBit(int col, int row, bool bit) => _bits[row * cols + col] = bit ? 1 : 0;

        public void Place()
        {
            int pos = 0, row = 4, col = 0;
            do
            {
                if (row == rows && col == 0) Corner1(pos++);
                if (row == rows - 2 && col == 0 && cols % 4 != 0) Corner2(pos++);
                if (row == rows - 2 && col == 0 && cols % 8 == 4) Corner3(pos++);
                if (row == rows + 4 && col == 2 && cols % 8 == 0) Corner4(pos++);

                do
                {
                    if (row < rows && col >= 0 && !HasBit(col, row)) Utah(row, col, pos++);
                    row -= 2;
                    col += 2;
                } while (row >= 0 && col < cols);
                row++;
                col += 3;

                do
                {
                    if (row >= 0 && col < cols && !HasBit(col, row)) Utah(row, col, pos++);
                    row += 2;
                    col -= 2;
                } while (row < rows && col >= 0);
                row += 3;
                col++;
            } while (row < rows || col < cols);

            if (!HasBit(cols - 1, rows - 1))
            {
                SetBit(cols - 1, rows - 1, true);
                SetBit(cols - 2, rows - 2, true);
            }
        }

        private void Module(int row, int col, int pos, int bit)
        {
            if (row < 0) { row += rows; col += 4 - (rows + 4) % 8; }
            if (col < 0) { col += cols; row += 4 - (cols + 4) % 8; }
            int value = codewords[pos] & (1 << (8 - bit));
            SetBit(col, row, value != 0);
        }

        private void Utah(int row, int col, int pos)
        {
            Module(row - 2, col - 2, pos, 1);
            Module(row - 2, col - 1, pos, 2);
            Module(row - 1, col - 2, pos, 3);
            Module(row - 1, col - 1, pos, 4);
            Module(row - 1, col, pos, 5);
            Module(row, col - 2, pos, 6);
            Module(row, col - 1, pos, 7);
            Module(row, col, pos, 8);
        }

        private void Corner1(int pos)
        {
            Module(rows - 1, 0, pos, 1); Module(rows - 1, 1, pos, 2); Module(rows - 1, 2, pos, 3); Module(0, cols - 2, pos, 4);
            Module(0, cols - 1, pos, 5); Module(1, cols - 1, pos, 6); Module(2, cols - 1, pos, 7); Module(3, cols - 1, pos, 8);
        }

        private void Corner2(int pos)
        {
            Module(rows - 3, 0, pos, 1); Module(rows - 2, 0, pos, 2); Module(rows - 1, 0, pos, 3); Module(0, cols - 4, pos, 4);
            Module(0, cols - 3, pos, 5); Module(0, cols - 2, pos, 6); Module(0, cols - 1, pos, 7); Module(1, cols - 1, pos, 8);
        }

        private void Corner3(int pos)
        {
            Module(rows - 3, 0, pos, 1); Module(rows - 2, 0, pos, 2); Module(rows - 1, 0, pos, 3); Module(0, cols - 2, pos, 4);
            Module(0, cols - 1, pos, 5); Module(1, cols - 1, pos, 6); Module(2, cols - 1, pos, 7); Module(3, cols - 1, pos, 8);
        }

        private void Corner4(int pos)
        {
            Module(rows - 1, 0, pos, 1); Module(rows - 1, cols - 1, pos, 2); Module(0, cols - 3, pos, 3); Module(0, cols - 2, pos, 4);
            Module(0, cols - 1, pos, 5); Module(1, cols - 3, pos, 6); Module(1, cols - 2, pos, 7); Module(1, cols - 1, pos, 8);
        }
    }
}
