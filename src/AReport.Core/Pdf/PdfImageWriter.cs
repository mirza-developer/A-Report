using AReport.Imaging;

namespace AReport.Pdf;

/// <summary>Writes an image XObject. JPEG and most PNG types are embedded without re-encoding.</summary>
internal static class PdfImageWriter
{
    public static int Write(PdfFile file, ReportImage image, bool compress)
    {
        string basics = $"/Type /XObject /Subtype /Image /Width {image.WidthPx} /Height {image.HeightPx} /BitsPerComponent 8";

        if (image.Format == ImageFormat.Jpeg)
        {
            string space = image.Components == 1 ? "/DeviceGray" : "/DeviceRGB";
            return file.Add(PdfSyntax.RawStream($"{basics} /ColorSpace {space} /Filter /DCTDecode", image.Data));
        }

        string Params(int colors) =>
            $"/Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors {colors} /BitsPerComponent 8 /Columns {image.WidthPx} >>";

        switch (image.PngColorType)
        {
            case 0:
                return file.Add(PdfSyntax.RawStream($"{basics} /ColorSpace /DeviceGray {Params(1)}", image.PngIdat!));
            case 2:
                return file.Add(PdfSyntax.RawStream($"{basics} /ColorSpace /DeviceRGB {Params(3)}", image.PngIdat!));
            case 3:
            {
                var palette = image.PngPalette!;
                int entries = palette.Length / 3;
                string lookup = Convert.ToHexString(palette.AsSpan(0, entries * 3));
                return file.Add(PdfSyntax.RawStream($"{basics} /ColorSpace [/Indexed /DeviceRGB {entries - 1} <{lookup}>] {Params(1)}", image.PngIdat!));
            }
            default: // 6 = RGBA
            {
                var (rgb, alpha) = PngPixelConverter.SplitRgba(image);
                int mask = file.Add(PdfSyntax.Stream(
                    $"/Type /XObject /Subtype /Image /Width {image.WidthPx} /Height {image.HeightPx} /ColorSpace /DeviceGray /BitsPerComponent 8",
                    alpha, compress));
                return file.Add(PdfSyntax.Stream($"{basics} /ColorSpace /DeviceRGB /SMask {mask} 0 R", rgb, compress));
            }
        }
    }
}
