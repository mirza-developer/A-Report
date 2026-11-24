using AReport.Barcodes;
using AReport.Layout;
using AReport.Model;

namespace AReport.Core.Tests;

public class BarcodeMatrixTests
{
    [Fact]
    public void Stores_modules_and_lists_dark_runs()
    {
        var m = new BarcodeMatrix(5, 2);
        m[0, 0] = true; m[1, 0] = true; m[3, 0] = true; m[4, 1] = true;
        Assert.True(m[1, 0]);
        Assert.False(m[2, 0]);
        Assert.Equal(new[] { (0, 0, 2), (0, 3, 1), (1, 4, 1) }, m.DarkRuns());
    }
}

public class QrEncoderTests
{
    private static string Bits(BarcodeMatrix m, int row, int from, int count) =>
        string.Concat(Enumerable.Range(from, count).Select(c => m[c, row] ? '1' : '0'));

    [Fact]
    public void Reproduces_the_codewords_of_the_iso_18004_worked_example()
    {
        // "01234567" at level M, version 1: data codewords then 10 error-correction codewords
        var ecc = QrEccLevel.Medium;
        var codewords = QrEncoder.BuildCodewords("01234567", ref ecc, boostEcc: false, out int version);

        Assert.Equal(1, version);
        Assert.Equal(new byte[]
        {
            0x10, 0x20, 0x0C, 0x56, 0x61, 0x80, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11,
            0xA5, 0x24, 0xD4, 0xC1, 0xED, 0x36, 0xC7, 0x87, 0x2C, 0x55,
        }, codewords);
    }

    [Theory]
    [InlineData(41, 21)]   // numeric capacity of version 1-L is 41 digits
    [InlineData(42, 25)]
    public void Numeric_capacity_decides_the_version(int digits, int size) =>
        Assert.Equal(size, QrEncoder.Encode(new string('1', digits), QrEccLevel.Low, boostEcc: false).Columns);

    [Theory]
    [InlineData(25, 21)]   // alphanumeric 1-L: 25 characters
    [InlineData(26, 25)]
    public void Alphanumeric_capacity_decides_the_version(int chars, int size) =>
        Assert.Equal(size, QrEncoder.Encode(new string('A', chars), QrEccLevel.Low, boostEcc: false).Columns);

    [Theory]
    [InlineData(17, 21)]   // byte 1-L: 17 bytes
    [InlineData(18, 25)]
    public void Byte_capacity_decides_the_version(int bytes, int size) =>
        Assert.Equal(size, QrEncoder.Encode(new string('a', bytes), QrEccLevel.Low, boostEcc: false).Columns);

    [Fact]
    public void Higher_error_correction_needs_a_bigger_symbol()
    {
        string text = new('a', 60);
        int low = QrEncoder.Encode(text, QrEccLevel.Low, false).Columns, high = QrEncoder.Encode(text, QrEccLevel.High, false).Columns;
        Assert.True(high > low);
    }

    [Fact]
    public void The_error_correction_level_is_raised_when_it_costs_nothing()
    {
        var level = QrEccLevel.Low;
        QrEncoder.BuildCodewords("HELLO", ref level, boostEcc: true, out _);
        Assert.Equal(QrEccLevel.High, level);
        level = QrEccLevel.Low;
        QrEncoder.BuildCodewords("HELLO", ref level, boostEcc: false, out _);
        Assert.Equal(QrEccLevel.Low, level);
    }

    [Fact]
    public void Has_the_three_finder_patterns_timing_lines_and_dark_module()
    {
        var m = QrEncoder.Encode("HELLO WORLD", QrEccLevel.Medium);
        int n = m.Columns;
        Assert.Equal(21, n);

        foreach (var (x0, y0) in new[] { (0, 0), (n - 7, 0), (0, n - 7) })
        {
            Assert.Equal("1111111", Bits(m, y0, x0, 7));
            Assert.Equal("1000001", Bits(m, y0 + 1, x0, 7));
            Assert.Equal("1011101", Bits(m, y0 + 2, x0, 7));
            Assert.Equal("1111111", Bits(m, y0 + 6, x0, 7));
        }

        for (int i = 8; i < n - 8; i++)
        {
            Assert.Equal(i % 2 == 0, m[i, 6]);
            Assert.Equal(i % 2 == 0, m[6, i]);
        }
        Assert.True(m[8, n - 8]);
    }

    [Theory]
    [InlineData(QrEccLevel.Low, 1)]
    [InlineData(QrEccLevel.Medium, 0)]
    [InlineData(QrEccLevel.Quartile, 3)]
    [InlineData(QrEccLevel.High, 2)]
    public void The_format_information_is_a_valid_bch_codeword_for_the_level(QrEccLevel level, int levelBits)
    {
        var m = QrEncoder.Encode("HELLO WORLD", level, boostEcc: false);
        int bits = 0;
        for (int i = 0; i <= 5; i++) bits |= (m[8, i] ? 1 : 0) << i;
        bits |= (m[8, 7] ? 1 : 0) << 6;
        bits |= (m[8, 8] ? 1 : 0) << 7;
        bits |= (m[7, 8] ? 1 : 0) << 8;
        for (int i = 9; i < 15; i++) bits |= (m[14 - i, 8] ? 1 : 0) << i;

        int data = (bits ^ 0x5412) >> 10;
        Assert.Equal(levelBits, data >> 3);

        int remainder = data;
        for (int i = 0; i < 10; i++) remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        Assert.Equal((bits ^ 0x5412) & 0x3FF, remainder & 0x3FF);
    }

    [Fact]
    public void Larger_versions_carry_version_information_and_alignment_patterns()
    {
        var m = QrEncoder.Encode(new string('a', 150), QrEccLevel.Medium, boostEcc: false);
        Assert.True(m.Columns >= 45);   // version 7 or more
        int n = m.Columns;
        // an alignment pattern: 5x5 with dark ring, light ring, dark centre, around the bottom-right anchor (n-7, n-7)
        Assert.Equal("11111", Bits(m, n - 9, n - 9, 5));
        Assert.Equal("10001", Bits(m, n - 8, n - 9, 5));
        Assert.Equal("10101", Bits(m, n - 7, n - 9, 5));
    }

    [Fact]
    public void Is_deterministic_and_handles_unicode_and_empty_text()
    {
        var a = QrEncoder.Encode("سلام 你好 €");
        var b = QrEncoder.Encode("سلام 你好 €");
        Assert.Equal(a.Columns, b.Columns);
        for (int y = 0; y < a.Rows; y++)
            for (int x = 0; x < a.Columns; x++) Assert.Equal(a[x, y], b[x, y]);
        Assert.Equal(21, QrEncoder.Encode("").Columns);
    }

    [Fact]
    public void Rejects_null_and_text_that_is_too_long()
    {
        Assert.Throws<ArgumentNullException>(() => QrEncoder.Encode(null!));
        Assert.Throws<ArgumentException>(() => QrEncoder.Encode(new string('a', 3000), QrEccLevel.Low, false));
    }

    [Fact]
    public void The_largest_version_is_40()
    {
        Assert.Equal(177, QrEncoder.Encode(new string('1', 7089), QrEccLevel.Low, false).Columns);
    }
}

public class DataMatrixEncoderTests
{
    [Fact]
    public void Reproduces_the_codewords_of_the_iso_16022_worked_example()
    {
        // "123456" = three digit pairs, exactly filling a 10x10 symbol, plus its 5 error-correction codewords
        Assert.Equal(new[] { 142, 164, 186, 114, 25, 5, 88, 102 }, DataMatrixEncoder.BuildCodewords("123456"));
    }

    [Fact]
    public void Pads_with_the_standard_pad_codewords()
    {
        var codewords = DataMatrixEncoder.BuildCodewords("A");
        Assert.Equal(new[] { 66, 129, 70 }, codewords[..3]);   // 'A'+1, first pad 129, then the randomised pad
        Assert.Equal(8, codewords.Length);
    }

    [Theory]
    [InlineData("A", 10)]
    [InlineData("ABC", 10)]
    [InlineData("ABCD", 12)]
    [InlineData("1234567890", 12)]       // digit pairs: 5 codewords
    [InlineData("INV-2026-0042", 16)]
    public void Chooses_the_smallest_square_symbol(string text, int size)
    {
        var m = DataMatrixEncoder.Encode(text);
        Assert.Equal((size, size), (m.Columns, m.Rows));
    }

    [Fact]
    public void Has_the_solid_l_shaped_finder_and_alternating_timing_pattern()
    {
        var m = DataMatrixEncoder.Encode("Hello, A-Report!");
        int n = m.Columns;
        for (int i = 0; i < n; i++)
        {
            Assert.True(m[0, i], $"left edge {i}");
            Assert.True(m[i, n - 1], $"bottom edge {i}");
            Assert.Equal(i % 2 == 0, m[i, 0]);            // top edge: dark, light, dark…
        }
        for (int y = 1; y < n - 1; y++) Assert.Equal((y - 1) % 2 == 0, m[n - 1, y]);   // right edge alternates
    }

    [Fact]
    public void Large_symbols_are_split_into_regions_with_their_own_finder_patterns()
    {
        var m = DataMatrixEncoder.Encode(new string('x', 50));   // 50 codewords: a 32x32 symbol of 2x2 regions of 16 modules
        Assert.Equal(32, m.Columns);
        for (int y = 0; y < 32; y++) Assert.True(m[16, y], $"left finder column of the right-hand regions, row {y}");
        for (int x = 0; x < 32; x++) Assert.True(m[x, 15], $"bottom finder row of the upper regions, column {x}");
        for (int x = 0; x < 32; x++) Assert.Equal(x % 2 == 0, m[x, 16]);   // top timing row of the lower regions
    }

    [Fact]
    public void Non_ascii_text_is_encoded_as_utf8_bytes()
    {
        var m = DataMatrixEncoder.Encode("é");
        Assert.True(m.Columns >= 10);
        var codewords = DataMatrixEncoder.BuildCodewords("é");
        Assert.Equal(235, codewords[0]);   // upper shift
    }

    [Fact]
    public void Capacity_limit_and_invalid_input()
    {
        Assert.Equal(144, DataMatrixEncoder.Encode(new string('x', 1558)).Columns);
        Assert.Throws<ArgumentException>(() => DataMatrixEncoder.Encode(new string('x', 1559)));
        Assert.Throws<ArgumentException>(() => DataMatrixEncoder.Encode(""));
        Assert.Throws<ArgumentNullException>(() => DataMatrixEncoder.Encode(null!));
    }
}

public class Code128EncoderTests
{
    private static string Modules(BarcodeMatrix m) => string.Concat(Enumerable.Range(0, m.Columns).Select(c => m[c, 0] ? '1' : '0'));

    [Fact]
    public void Encodes_text_in_code_set_b_with_the_standard_checksum()
    {
        // start B(104) H(40) e(69) l(76) l(76) o(79); checksum = (104 + 40*1 + 69*2 + 76*3 + 76*4 + 79*5) % 103 = 76
        Assert.Equal(new[] { 104, 40, 69, 76, 76, 79, 76, 106 }, Code128Encoder.Encode128Values("Hello"));
    }

    [Fact]
    public void Packs_digit_pairs_in_code_set_c()
    {
        Assert.Equal(new[] { 105, 12, 34, 56, 78 }, Code128Encoder.Encode128Values("12345678").Take(5));
        Assert.Equal(106, Code128Encoder.Encode128Values("12345678")[^1]);
    }

    [Fact]
    public void Switches_between_code_sets_when_digits_start_and_stop()
    {
        // B 'A' ; C 12 34 ; B 'B'
        var values = Code128Encoder.Encode128Values("A1234B");
        Assert.Equal(new[] { 104, 33, 99, 12, 34, 100, 34, 78, 106 }, values);
    }

    [Fact]
    public void A_short_all_digit_text_uses_code_set_c_and_an_odd_digit_falls_back_to_b()
    {
        Assert.Equal(105, Code128Encoder.Encode128Values("12")[0]);
        var odd = Code128Encoder.Encode128Values("123");
        Assert.Equal(new[] { 105, 12, 100, 19 }, odd.Take(4));   // C: 12, then B: '3' = 51 - 32 = 19
    }

    [Fact]
    public void The_symbol_has_the_expected_width_and_start_and_stop_patterns()
    {
        var m = Code128Encoder.Encode("Hello");
        Assert.Equal(1, m.Rows);
        Assert.Equal(11 * 7 + 13, m.Columns);                      // 7 symbols of 11 modules + a 13-module stop
        Assert.StartsWith("11010010000", Modules(m));              // start code B
        Assert.EndsWith("1100011101011", Modules(m));              // stop pattern with its termination bar
    }

    [Fact]
    public void Rejects_empty_text_and_characters_outside_printable_ascii()
    {
        Assert.Throws<ArgumentException>(() => Code128Encoder.Encode(""));
        Assert.Throws<ArgumentException>(() => Code128Encoder.Encode("é"));
        Assert.Throws<ArgumentException>(() => Code128Encoder.Encode("a\nb"));
        Assert.Contains("U+00E9", Assert.Throws<ArgumentException>(() => Code128Encoder.Encode("é")).Message);
    }
}

public class ShapeGeometryTests
{
    [Theory]
    [InlineData(ShapeKind.Triangle, 3)]
    [InlineData(ShapeKind.RightTriangle, 3)]
    [InlineData(ShapeKind.Diamond, 4)]
    [InlineData(ShapeKind.Pentagon, 5)]
    [InlineData(ShapeKind.Hexagon, 6)]
    [InlineData(ShapeKind.ArrowRight, 7)]
    [InlineData(ShapeKind.Star, 10)]
    public void Polygons_have_the_right_number_of_points_inside_the_unit_square(ShapeKind kind, int points)
    {
        var polygon = ShapeGeometry.Polygon(kind)!;
        Assert.Equal(points, polygon.Length);
        Assert.All(polygon, p => { Assert.InRange(p.X, -1e-9, 1 + 1e-9); Assert.InRange(p.Y, -1e-9, 1 + 1e-9); });
    }

    [Theory]
    [InlineData(ShapeKind.Pentagon)]
    [InlineData(ShapeKind.Star)]
    public void Regular_shapes_fill_their_bounding_box(ShapeKind kind)
    {
        var polygon = ShapeGeometry.Polygon(kind)!;
        Assert.Equal(0, polygon.Min(p => p.X), 6);
        Assert.Equal(1, polygon.Max(p => p.X), 6);
        Assert.Equal(0, polygon.Min(p => p.Y), 6);
        Assert.Equal(1, polygon.Max(p => p.Y), 6);
    }

    [Fact]
    public void Curved_shapes_have_no_polygon()
    {
        Assert.Null(ShapeGeometry.Polygon(ShapeKind.Rectangle));
        Assert.Null(ShapeGeometry.Polygon(ShapeKind.Ellipse));
        Assert.Null(ShapeGeometry.Polygon(ShapeKind.RoundedRectangle));
    }

    [Fact]
    public void Arrows_point_in_their_direction()
    {
        Assert.Equal(1, ShapeGeometry.Polygon(ShapeKind.ArrowRight)!.Max(p => p.X));
        Assert.Equal(0, ShapeGeometry.Polygon(ShapeKind.ArrowLeft)!.Min(p => p.X));
        Assert.Equal(0, ShapeGeometry.Polygon(ShapeKind.ArrowUp)!.Min(p => p.Y));
        Assert.Equal(1, ShapeGeometry.Polygon(ShapeKind.ArrowDown)!.Max(p => p.Y));
    }
}
