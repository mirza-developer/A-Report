using System.IO.Compression;

namespace AReport.Core.Tests.Support;

/// <summary>Builds tiny valid PNG files and header-only JPEG files for tests.</summary>
internal static class TestImages
{
    /// <summary>
    /// Builds an 8-bit PNG. <paramref name="pixel"/> returns the raw bytes of one pixel
    /// (1 for gray/palette, 3 for RGB, 4 for RGBA). <paramref name="filter"/> is 0 (None), 1 (Sub) or 2 (Up).
    /// </summary>
    public static byte[] Png(int width, int height, int colorType, Func<int, int, byte[]> pixel, int filter = 0, byte[]? palette = null)
    {
        int bpp = colorType switch { 0 => 1, 2 => 3, 3 => 1, 6 => 4, _ => throw new ArgumentOutOfRangeException(nameof(colorType)) };
        int stride = width * bpp;

        var raw = new byte[height * stride];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixel(x, y).CopyTo(raw, y * stride + x * bpp);

        var scan = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            scan.WriteByte((byte)filter);
            for (int i = 0; i < stride; i++)
            {
                int cur = raw[y * stride + i];
                int left = i >= bpp ? raw[y * stride + i - bpp] : 0;
                int up = y > 0 ? raw[(y - 1) * stride + i] : 0;
                int sub = filter switch { 1 => left, 2 => up, _ => 0 };
                scan.WriteByte((byte)(cur - sub));
            }
        }

        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteInt(ihdr, 0, width);
        WriteInt(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = (byte)colorType;
        Chunk(png, "IHDR", ihdr);
        if (palette is not null) Chunk(png, "PLTE", palette);

        var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(scan.ToArray());
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>A JPEG with only the markers the reader needs (not decodable, which is fine for structure tests).</summary>
    public static byte[] Jpeg(int width, int height, int components = 3)
    {
        var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        ms.Write([0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        int length = 8 + 3 * components;
        ms.Write([0xFF, 0xC0, (byte)(length >> 8), (byte)length, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components]);
        for (int c = 1; c <= components; c++) ms.Write([(byte)c, 0x11, 0]);
        ms.Write([0xFF, 0xD9]);
        return ms.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcInput = typeBytes.Concat(data).ToArray();
        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc32(crcInput));
        stream.Write(crc);
    }

    private static void WriteInt(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }
}
