namespace AReport.Imaging;

public enum ImageFormat { Jpeg, Png }

/// <summary>A decoded-enough image: dimensions plus the bytes the PDF writer needs.</summary>
public sealed class ReportImage
{
    public ReportImage(ImageFormat format, int widthPx, int heightPx, byte[] data)
    {
        Format = format;
        WidthPx = widthPx;
        HeightPx = heightPx;
        Data = data;
    }

    public ImageFormat Format { get; }
    public int WidthPx { get; }
    public int HeightPx { get; }

    /// <summary>Original file bytes.</summary>
    public byte[] Data { get; }

    /// <summary>JPEG: 1 (gray) or 3 (RGB).</summary>
    public int Components { get; init; }

    /// <summary>PNG colour type: 0 gray, 2 RGB, 3 palette, 6 RGBA.</summary>
    public int PngColorType { get; init; }

    /// <summary>PNG: concatenated IDAT payload (a zlib stream).</summary>
    public byte[]? PngIdat { get; init; }

    /// <summary>PNG: RGB palette bytes.</summary>
    public byte[]? PngPalette { get; init; }
}
