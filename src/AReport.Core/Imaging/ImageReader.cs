using System.Buffers.Binary;

namespace AReport.Imaging;

/// <summary>Reads just enough of PNG and JPEG files to embed them in a PDF (no pixel decoding for JPEG).</summary>
public static class ImageReader
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool TryRead(byte[]? data, out ReportImage? image, out string error)
    {
        image = null;
        error = "";
        if (data is null || data.Length < 4)
        {
            error = "The image data is empty.";
            return false;
        }

        if (data.AsSpan(0, Math.Min(8, data.Length)).StartsWith(PngSignature))
            return TryReadPng(data, out image, out error);
        if (data[0] == 0xFF && data[1] == 0xD8)
            return TryReadJpeg(data, out image, out error);

        error = "Unsupported image format. Only PNG and JPEG are supported.";
        return false;
    }

    private static bool TryReadPng(byte[] data, out ReportImage? image, out string error)
    {
        image = null;
        error = "";
        int pos = 8;
        int width = 0, height = 0, depth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null;
        using var idat = new MemoryStream();

        while (pos + 8 <= data.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
            string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            int bodyStart = pos + 8;
            if (length > int.MaxValue || bodyStart + (long)length > data.Length)
            {
                error = "The PNG file is truncated.";
                return false;
            }

            var body = data.AsSpan(bodyStart, (int)length);
            switch (type)
            {
                case "IHDR" when length >= 13:
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(body[..4]);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(body.Slice(4, 4));
                    depth = body[8];
                    colorType = body[9];
                    interlace = body[12];
                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "IDAT":
                    idat.Write(body);
                    break;
            }

            if (type == "IEND") break;
            pos = bodyStart + (int)length + 4; // skip CRC
        }

        if (width <= 0 || height <= 0 || colorType < 0)
        {
            error = "The PNG file has no valid header.";
            return false;
        }
        if (depth != 8)
        {
            error = $"Unsupported PNG bit depth {depth}. Only 8-bit PNG is supported.";
            return false;
        }
        if (interlace != 0)
        {
            error = "Interlaced PNG is not supported.";
            return false;
        }
        if (colorType is not (0 or 2 or 3 or 6))
        {
            error = $"Unsupported PNG colour type {colorType}.";
            return false;
        }
        if (colorType == 3 && (palette is null || palette.Length < 3))
        {
            error = "The PNG palette is missing.";
            return false;
        }
        if (idat.Length == 0)
        {
            error = "The PNG file has no image data.";
            return false;
        }

        image = new ReportImage(ImageFormat.Png, width, height, data)
        {
            PngColorType = colorType,
            PngIdat = idat.ToArray(),
            PngPalette = palette,
        };
        return true;
    }

    private static bool TryReadJpeg(byte[] data, out ReportImage? image, out string error)
    {
        image = null;
        error = "";
        int pos = 2;
        while (pos + 4 <= data.Length)
        {
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            byte marker = data[pos + 1];
            if (marker == 0xFF) { pos++; continue; }                       // fill bytes
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { pos += 2; continue; } // no payload
            if (marker == 0xD9) break;                                      // EOI

            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2, 2));
            bool isSof = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof)
            {
                if (pos + 10 > data.Length || length < 8)
                {
                    error = "The JPEG file is truncated.";
                    return false;
                }
                int precision = data[pos + 4];
                int height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 5, 2));
                int width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 7, 2));
                int components = data[pos + 9];
                if (precision != 8)
                {
                    error = $"Unsupported JPEG precision {precision}.";
                    return false;
                }
                if (components is not (1 or 3))
                {
                    error = "Only grayscale and RGB JPEG images are supported (not CMYK).";
                    return false;
                }
                if (width <= 0 || height <= 0)
                {
                    error = "The JPEG file has invalid dimensions.";
                    return false;
                }
                image = new ReportImage(ImageFormat.Jpeg, width, height, data) { Components = components };
                return true;
            }

            pos += 2 + length;
        }

        error = "The JPEG file has no frame header.";
        return false;
    }
}
