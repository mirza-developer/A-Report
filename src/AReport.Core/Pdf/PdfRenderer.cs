using System.Text;
using AReport.Fonts;
using AReport.Imaging;
using AReport.Layout;
using AReport.Model;

namespace AReport.Pdf;

/// <summary>Writes a PDF 1.4 file using the standard fonts (WinAnsi text), JPEG/PNG images, vector shapes and clipped text.</summary>
public sealed class PdfRenderer(ReportOptions options, TimeProvider? time = null) : IReportRenderer
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string ContentType => "application/pdf";

    public string FileExtension => ".pdf";

    public byte[] Render(LaidOutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var file = new PdfFile();
        int catalog = file.Reserve();
        int pagesRoot = file.Reserve();
        int info = file.Reserve();

        var fonts = new Dictionary<string, (string Resource, int Id)>();
        var images = new Dictionary<ReportImage, (string Resource, int Id)>();

        var contentIds = new List<int>();
        foreach (var page in document.Pages)
        {
            byte[] content = BuildContent(page, file, fonts, images);
            contentIds.Add(file.Add(PdfSyntax.Stream("", content, options.CompressPdf)));
        }

        var resources = new StringBuilder("<< /ProcSet [/PDF /Text /ImageB /ImageC]");
        if (fonts.Count > 0)
            resources.Append(" /Font << ").AppendJoin(' ', fonts.Values.Select(f => $"/{f.Resource} {f.Id} 0 R")).Append(" >>");
        if (images.Count > 0)
            resources.Append(" /XObject << ").AppendJoin(' ', images.Values.Select(i => $"/{i.Resource} {i.Id} 0 R")).Append(" >>");
        resources.Append(" >>");
        int resourcesId = file.Add(resources.ToString());

        var pageIds = new List<int>();
        for (int i = 0; i < document.Pages.Count; i++)
        {
            var page = document.Pages[i];
            pageIds.Add(file.Add(
                $"<< /Type /Page /Parent {pagesRoot} 0 R /MediaBox [0 0 {PdfSyntax.Num(Units.MmToPt(page.WidthMm))} {PdfSyntax.Num(Units.MmToPt(page.HeightMm))}] " +
                $"/Resources {resourcesId} 0 R /Contents {contentIds[i]} 0 R >>"));
        }

        file.Set(pagesRoot, PdfSyntax.Ascii(
            $"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] >>"));
        file.Set(catalog, PdfSyntax.Ascii($"<< /Type /Catalog /Pages {pagesRoot} 0 R >>"));
        file.Set(info, PdfSyntax.Ascii(
            $"<< /Title {PdfSyntax.TextString(document.Title)} /Producer (A-Report) " +
            $"/CreationDate (D:{_time.GetUtcNow():yyyyMMddHHmmss}Z) >>"));

        return file.Build(catalog, info);
    }

    private byte[] BuildContent(LaidOutPage page, PdfFile file,
        Dictionary<string, (string Resource, int Id)> fonts, Dictionary<ReportImage, (string Resource, int Id)> images)
    {
        double pageHeightPt = Units.MmToPt(page.HeightMm);
        double X(double mm) => Units.MmToPt(mm);
        double Y(double mm) => pageHeightPt - Units.MmToPt(mm);

        using var ms = new MemoryStream();
        void Write(string text) => ms.Write(PdfSyntax.Ascii(text));
        string Rgb(RgbColor c) => $"{PdfSyntax.Num(c.R / 255.0)} {PdfSyntax.Num(c.G / 255.0)} {PdfSyntax.Num(c.B / 255.0)}";

        foreach (var item in page.Items)
        {
            switch (item)
            {
                case RectItem r:
                {
                    bool fill = r.Fill is not null, stroke = r.Stroke is not null && r.StrokeWidthMm > 0;
                    if (!fill && !stroke) break;
                    Write("q\n");
                    if (fill) Write($"{Rgb(r.Fill!.Value)} rg\n");
                    if (stroke) Write($"{Rgb(r.Stroke!.Value)} RG\n{PdfSyntax.Num(X(r.StrokeWidthMm))} w\n");
                    Write($"{PdfSyntax.Num(X(r.XMm))} {PdfSyntax.Num(Y(r.YMm + r.HeightMm))} {PdfSyntax.Num(X(r.WidthMm))} {PdfSyntax.Num(X(r.HeightMm))} re\n");
                    Write(fill && stroke ? "B\n" : fill ? "f\n" : "S\n");
                    Write("Q\n");
                    break;
                }
                case LineItem l:
                    Write($"q\n{Rgb(l.Color)} RG\n{PdfSyntax.Num(X(l.WidthMm))} w\n{PdfSyntax.Num(X(l.X1Mm))} {PdfSyntax.Num(Y(l.Y1Mm))} m {PdfSyntax.Num(X(l.X2Mm))} {PdfSyntax.Num(Y(l.Y2Mm))} l S\nQ\n");
                    break;
                case TextItem t:
                {
                    var lines = t.Lines.Where(line => line.Text.Length > 0).ToList();
                    if (lines.Count == 0) break;
                    string pdfName = StandardFonts.PdfName(t.Font);
                    if (!fonts.TryGetValue(pdfName, out var font))
                    {
                        int id = file.Add($"<< /Type /Font /Subtype /Type1 /BaseFont /{pdfName} /Encoding /WinAnsiEncoding >>");
                        font = ($"F{fonts.Count + 1}", id);
                        fonts[pdfName] = font;
                    }

                    Write("q\n");
                    Write($"{PdfSyntax.Num(X(t.XMm))} {PdfSyntax.Num(Y(t.YMm + t.HeightMm))} {PdfSyntax.Num(X(t.WidthMm))} {PdfSyntax.Num(X(t.HeightMm))} re W n\n");
                    Write($"BT\n/{font.Resource} {PdfSyntax.Num(t.Font.SizePt)} Tf\n{Rgb(t.Color)} rg\n");
                    foreach (var line in lines)
                    {
                        Write($"1 0 0 1 {PdfSyntax.Num(X(line.XMm))} {PdfSyntax.Num(Y(line.BaselineYMm))} Tm\n(");
                        ms.Write(EscapeLiteral(WinAnsi.Encode(line.Text)));
                        Write(") Tj\n");
                    }
                    Write("ET\nQ\n");
                    break;
                }
                case ImageItem i:
                {
                    if (!images.TryGetValue(i.Image, out var image))
                    {
                        int id = PdfImageWriter.Write(file, i.Image, options.CompressPdf);
                        image = ($"Im{images.Count + 1}", id);
                        images[i.Image] = image;
                    }
                    Write($"q\n{PdfSyntax.Num(X(i.WidthMm))} 0 0 {PdfSyntax.Num(X(i.HeightMm))} {PdfSyntax.Num(X(i.XMm))} {PdfSyntax.Num(Y(i.YMm + i.HeightMm))} cm\n/{image.Resource} Do\nQ\n");
                    break;
                }
            }
        }
        return ms.ToArray();
    }

    private static byte[] EscapeLiteral(byte[] bytes)
    {
        var result = new List<byte>(bytes.Length + 4);
        foreach (var b in bytes)
        {
            if (b is (byte)'\\' or (byte)'(' or (byte)')') result.Add((byte)'\\');
            result.Add(b);
        }
        return result.ToArray();
    }
}
