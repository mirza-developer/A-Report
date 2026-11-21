using AReport.Core.Tests.Support;
using AReport.Imaging;

namespace AReport.Core.Tests;

public class ImageReaderTests
{
    private static ReportImage Read(byte[] data)
    {
        Assert.True(ImageReader.TryRead(data, out var image, out var error), error);
        return image!;
    }

    private static string Fail(byte[]? data)
    {
        Assert.False(ImageReader.TryRead(data, out var image, out var error));
        Assert.Null(image);
        return error;
    }

    [Fact]
    public void Reads_rgb_png()
    {
        var image = Read(TestImages.Png(3, 2, 2, (_, _) => [1, 2, 3]));
        Assert.Equal(ImageFormat.Png, image.Format);
        Assert.Equal(3, image.WidthPx);
        Assert.Equal(2, image.HeightPx);
        Assert.Equal(2, image.PngColorType);
        Assert.NotEmpty(image.PngIdat!);
    }

    [Fact]
    public void Reads_gray_and_rgba_png()
    {
        Assert.Equal(0, Read(TestImages.Png(2, 2, 0, (_, _) => [5])).PngColorType);
        Assert.Equal(6, Read(TestImages.Png(2, 2, 6, (_, _) => [1, 2, 3, 4])).PngColorType);
    }

    [Fact]
    public void Reads_palette_png_with_its_palette()
    {
        var image = Read(TestImages.Png(2, 1, 3, (x, _) => [(byte)x], palette: [255, 0, 0, 0, 255, 0]));
        Assert.Equal(3, image.PngColorType);
        Assert.Equal(6, image.PngPalette!.Length);
    }

    [Fact]
    public void Palette_png_without_a_palette_is_rejected() =>
        Assert.Contains("palette", Fail(TestImages.Png(1, 1, 3, (_, _) => [0])));

    [Fact]
    public void Reads_jpeg_dimensions_and_components()
    {
        var rgb = Read(TestImages.Jpeg(640, 480));
        Assert.Equal(ImageFormat.Jpeg, rgb.Format);
        Assert.Equal(640, rgb.WidthPx);
        Assert.Equal(480, rgb.HeightPx);
        Assert.Equal(3, rgb.Components);
        Assert.Equal(1, Read(TestImages.Jpeg(10, 10, 1)).Components);
    }

    [Fact]
    public void Cmyk_jpeg_is_rejected() => Assert.Contains("CMYK", Fail(TestImages.Jpeg(10, 10, 4)));

    [Fact]
    public void Unknown_formats_are_rejected()
    {
        Assert.Contains("Unsupported image format", Fail("GIF89a....."u8.ToArray()));
        Assert.Contains("empty", Fail([]));
        Assert.Contains("empty", Fail(null));
    }

    [Fact]
    public void Sixteen_bit_png_is_rejected()
    {
        var png = TestImages.Png(1, 1, 2, (_, _) => [1, 2, 3]);
        png[24] = 16; // bit depth byte inside IHDR
        Assert.Contains("bit depth", Fail(png));
    }

    [Fact]
    public void Interlaced_png_is_rejected()
    {
        var png = TestImages.Png(1, 1, 2, (_, _) => [1, 2, 3]);
        png[28] = 1; // interlace byte inside IHDR
        Assert.Contains("Interlaced", Fail(png));
    }

    [Fact]
    public void Truncated_png_is_rejected()
    {
        var png = TestImages.Png(4, 4, 2, (_, _) => [1, 2, 3]);
        Assert.Contains("truncated", Fail(png[..45]));
    }

    [Fact]
    public void Jpeg_without_a_frame_header_is_rejected() =>
        Assert.Contains("frame header", Fail([0xFF, 0xD8, 0xFF, 0xD9, 0x00, 0x00]));
}

public class PngPixelConverterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Splits_rgba_into_color_and_alpha_for_every_supported_filter(int filter)
    {
        byte[] Pixel(int x, int y) => [(byte)(x * 10 + y), (byte)(x * 20 + 3), (byte)(200 - x - y), (byte)(255 - x * 40 - y * 5)];
        var png = TestImages.Png(4, 3, 6, Pixel, filter);
        Assert.True(ImageReader.TryRead(png, out var image, out var error), error);

        var (rgb, alpha) = PngPixelConverter.SplitRgba(image!);

        Assert.Equal(4 * 3 * 3, rgb.Length);
        Assert.Equal(4 * 3, alpha.Length);
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                var expected = Pixel(x, y);
                int i = y * 4 + x;
                Assert.Equal(expected[0], rgb[i * 3]);
                Assert.Equal(expected[1], rgb[i * 3 + 1]);
                Assert.Equal(expected[2], rgb[i * 3 + 2]);
                Assert.Equal(expected[3], alpha[i]);
            }
        }
    }

    [Fact]
    public void Rejects_non_rgba_images()
    {
        Assert.True(ImageReader.TryRead(TestImages.Png(1, 1, 2, (_, _) => [1, 2, 3]), out var image, out _));
        Assert.Throws<ArgumentException>(() => PngPixelConverter.SplitRgba(image!));
    }
}
