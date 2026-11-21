using System.IO.Compression;

namespace AReport.Imaging;

/// <summary>Splits an RGBA PNG into separate colour and alpha planes (PDF stores alpha as a soft mask).</summary>
public static class PngPixelConverter
{
    public static (byte[] Rgb, byte[] Alpha) SplitRgba(ReportImage image)
    {
        if (image.Format != ImageFormat.Png || image.PngColorType != 6 || image.PngIdat is null)
            throw new ArgumentException("The image is not an RGBA PNG.", nameof(image));

        const int bpp = 4;
        int width = image.WidthPx, height = image.HeightPx;
        int stride = width * bpp;

        byte[] raw = new byte[(stride + 1) * height];
        using (var zlib = new ZLibStream(new MemoryStream(image.PngIdat), CompressionMode.Decompress))
        {
            int read = 0;
            while (read < raw.Length)
            {
                int n = zlib.Read(raw, read, raw.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read < raw.Length)
                throw new InvalidDataException("The PNG image data is shorter than its header declares.");
        }

        byte[] rgb = new byte[width * height * 3];
        byte[] alpha = new byte[width * height];
        byte[] prev = new byte[stride];
        byte[] cur = new byte[stride];

        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
            Unfilter(filter, cur, prev, bpp);

            for (int x = 0; x < width; x++)
            {
                int s = x * bpp;
                int d = (y * width + x) * 3;
                rgb[d] = cur[s];
                rgb[d + 1] = cur[s + 1];
                rgb[d + 2] = cur[s + 2];
                alpha[y * width + x] = cur[s + 3];
            }

            (prev, cur) = (cur, prev);
        }

        return (rgb, alpha);
    }

    private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            int add = filter switch
            {
                0 => 0,
                1 => a,
                2 => b,
                3 => (a + b) / 2,
                4 => Paeth(a, b, c),
                _ => throw new InvalidDataException($"Invalid PNG filter type {filter}."),
            };
            cur[i] = (byte)(cur[i] + add);
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }
}
