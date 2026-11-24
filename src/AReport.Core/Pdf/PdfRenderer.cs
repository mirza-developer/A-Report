using System.Text;
using AReport.Barcodes;
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

        var fonts = new PdfFontResources(file, options.CompressPdf);
        var images = new Dictionary<ReportImage, (string Resource, int Id)>();

        var contentIds = new List<int>();
        foreach (var page in document.Pages)
        {
            byte[] content = BuildContent(page, file, fonts, images);
            contentIds.Add(file.Add(PdfSyntax.Stream("", content, options.CompressPdf)));
        }

        fonts.Write();
        var resources = new StringBuilder("<< /ProcSet [/PDF /Text /ImageB /ImageC]");
        if (!fonts.IsEmpty)
            resources.Append(' ').Append(fonts.ResourceDictionary());
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
        PdfFontResources fonts, Dictionary<ReportImage, (string Resource, int Id)> images)
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

                    Write("q\n");
                    if (Math.Abs(t.RotationDeg) > 1e-9)
                    {
                        double cx = X(t.XMm + t.WidthMm / 2), cy = Y(t.YMm + t.HeightMm / 2);
                        double radians = t.RotationDeg * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
                        Write($"1 0 0 1 {PdfSyntax.Num(cx)} {PdfSyntax.Num(cy)} cm\n{PdfSyntax.Num(cos)} {PdfSyntax.Num(sin)} {PdfSyntax.Num(-sin)} {PdfSyntax.Num(cos)} 0 0 cm\n1 0 0 1 {PdfSyntax.Num(-cx)} {PdfSyntax.Num(-cy)} cm\n");
                    }
                    Write($"{PdfSyntax.Num(X(t.XMm))} {PdfSyntax.Num(Y(t.YMm + t.HeightMm))} {PdfSyntax.Num(X(t.WidthMm))} {PdfSyntax.Num(X(t.HeightMm))} re W n\n");
                    foreach (var line in lines)
                    {
                        if (line.Runs is null) WriteLegacyLine(ms, fonts, t, line, X, Y, Rgb);
                        else WriteRuns(ms, fonts, t, line, X, Y, Rgb);
                    }
                    Write("Q\n");
                    break;
                }
                case ShapeItem shape:
                    WriteShape(ms, shape, X, Y, Rgb);
                    break;
                case BarcodeItem barcode:
                    WriteBarcode(ms, barcode, X, Y, Rgb);
                    break;
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


    private const double Kappa = 0.5522847498;

    private static void WriteShape(MemoryStream ms, ShapeItem shape, Func<double, double> x, Func<double, double> y, Func<RgbColor, string> rgb)
    {
        bool fill = shape.Fill is not null, stroke = shape.Stroke is not null && shape.StrokeWidthMm > 0;
        if (!fill && !stroke) return;

        double left = x(shape.XMm), right = x(shape.XMm + shape.WidthMm);
        double top = y(shape.YMm), bottom = y(shape.YMm + shape.HeightMm);   // PDF: top > bottom
        double w = right - left, h = top - bottom;

        var path = new StringBuilder();
        string N(double v) => PdfSyntax.Num(v);

        switch (shape.Kind)
        {
            case ShapeKind.Rectangle:
                path.Append($"{N(left)} {N(bottom)} {N(w)} {N(h)} re\n");
                break;

            case ShapeKind.RoundedRectangle:
            {
                double r = Math.Min(Units.MmToPt(Math.Max(0, shape.CornerRadiusMm)), Math.Min(w, h) / 2), k = r * Kappa;
                path.Append($"{N(left + r)} {N(bottom)} m\n{N(right - r)} {N(bottom)} l\n");
                path.Append($"{N(right - r + k)} {N(bottom)} {N(right)} {N(bottom + r - k)} {N(right)} {N(bottom + r)} c\n");
                path.Append($"{N(right)} {N(top - r)} l\n");
                path.Append($"{N(right)} {N(top - r + k)} {N(right - r + k)} {N(top)} {N(right - r)} {N(top)} c\n");
                path.Append($"{N(left + r)} {N(top)} l\n");
                path.Append($"{N(left + r - k)} {N(top)} {N(left)} {N(top - r + k)} {N(left)} {N(top - r)} c\n");
                path.Append($"{N(left)} {N(bottom + r)} l\n");
                path.Append($"{N(left)} {N(bottom + r - k)} {N(left + r - k)} {N(bottom)} {N(left + r)} {N(bottom)} c\nh\n");
                break;
            }

            case ShapeKind.Ellipse:
            {
                double cx = (left + right) / 2, cy = (top + bottom) / 2, rx = w / 2, ry = h / 2, kx = rx * Kappa, ky = ry * Kappa;
                path.Append($"{N(cx + rx)} {N(cy)} m\n");
                path.Append($"{N(cx + rx)} {N(cy + ky)} {N(cx + kx)} {N(cy + ry)} {N(cx)} {N(cy + ry)} c\n");
                path.Append($"{N(cx - kx)} {N(cy + ry)} {N(cx - rx)} {N(cy + ky)} {N(cx - rx)} {N(cy)} c\n");
                path.Append($"{N(cx - rx)} {N(cy - ky)} {N(cx - kx)} {N(cy - ry)} {N(cx)} {N(cy - ry)} c\n");
                path.Append($"{N(cx + kx)} {N(cy - ry)} {N(cx + rx)} {N(cy - ky)} {N(cx + rx)} {N(cy)} c\nh\n");
                break;
            }

            default:
            {
                var points = ShapeGeometry.Polygon(shape.Kind);
                if (points is null) return;
                for (int i = 0; i < points.Length; i++)
                    path.Append($"{N(left + points[i].X * w)} {N(top - points[i].Y * h)} {(i == 0 ? "m" : "l")}\n");
                path.Append("h\n");
                break;
            }
        }

        var sb = new StringBuilder("q\n");
        if (fill) sb.Append(rgb(shape.Fill!.Value)).Append(" rg\n");
        if (stroke) sb.Append(rgb(shape.Stroke!.Value)).Append(" RG\n").Append(N(Units.MmToPt(shape.StrokeWidthMm))).Append(" w\n");
        sb.Append(path).Append(fill && stroke ? "B\n" : fill ? "f\n" : "S\n").Append("Q\n");
        ms.Write(PdfSyntax.Ascii(sb.ToString()));
    }

    /// <summary>Dark modules as rectangles; identical runs in consecutive rows are merged into one taller rectangle.</summary>
    private static void WriteBarcode(MemoryStream ms, BarcodeItem item, Func<double, double> x, Func<double, double> y, Func<RgbColor, string> rgb)
    {
        var matrix = item.Matrix;
        double moduleW = Units.MmToPt(item.WidthMm) / matrix.Columns, moduleH = Units.MmToPt(item.HeightMm) / matrix.Rows;
        double left = x(item.XMm), top = y(item.YMm);

        var sb = new StringBuilder($"q\n{rgb(item.Color)} rg\n");
        var open = new Dictionary<(int Start, int Length), int>();
        void Flush((int Start, int Length) run, int startRow, int endRowExclusive) =>
            sb.Append($"{PdfSyntax.Num(left + run.Start * moduleW)} {PdfSyntax.Num(top - endRowExclusive * moduleH)} {PdfSyntax.Num(run.Length * moduleW)} {PdfSyntax.Num((endRowExclusive - startRow) * moduleH)} re\n");

        var runsByRow = matrix.DarkRuns().GroupBy(r => r.Row).ToDictionary(g => g.Key, g => g.Select(r => (r.Start, r.Length)).ToHashSet());
        for (int row = 0; row <= matrix.Rows; row++)
        {
            var current = runsByRow.TryGetValue(row, out var set) && row < matrix.Rows ? set : [];
            foreach (var run in open.Keys.ToList())
            {
                if (current.Contains(run)) continue;
                Flush(run, open[run], row);
                open.Remove(run);
            }
            foreach (var run in current) open.TryAdd(run, row);
        }
        sb.Append("f\nQ\n");
        ms.Write(PdfSyntax.Ascii(sb.ToString()));
    }

    private static void Put(MemoryStream ms, string text) => ms.Write(PdfSyntax.Ascii(text));

    /// <summary>A line without shaped glyphs: WinAnsi text in the item's standard font.</summary>
    private static void WriteLegacyLine(MemoryStream ms, PdfFontResources fonts, TextItem item, TextLine line,
        Func<double, double> x, Func<double, double> y, Func<RgbColor, string> rgb)
    {
        var resource = fonts.Use(new StandardFace(item.Font));
        Put(ms, $"BT\n/{resource} {PdfSyntax.Num(item.Font.SizePt)} Tf\n{rgb(item.Color)} rg\n");
        Put(ms, $"1 0 0 1 {PdfSyntax.Num(x(line.XMm))} {PdfSyntax.Num(y(line.BaselineYMm))} Tm\n(");
        ms.Write(EscapeLiteral(WinAnsi.Encode(line.Text)));
        Put(ms, ") Tj\nET\n");
    }

    /// <summary>Shaped runs, left to right. TrueType runs use 2-byte glyph ids; RTL and shaped runs carry their logical text.</summary>
    private static void WriteRuns(MemoryStream ms, PdfFontResources fonts, TextItem item, TextLine line,
        Func<double, double> x, Func<double, double> y, Func<RgbColor, string> rgb)
    {
        double size = item.Font.SizePt;
        double penMm = line.XMm;

        foreach (var run in line.Runs!)
        {
            var resource = fonts.Use(run);
            double widthPt = run.Advances1000.Sum() * size / 1000.0;
            bool trueType = run.Face is TrueTypeFace;
            bool shaped = trueType && (run.IsRightToLeft || run.Sources is { } s && s.Any(src => src.Length != 1 || src[0] > 0x7F));

            if (shaped)
                Put(ms, "/Span << /ActualText <FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(run.Text)) + "> >> BDC\n");

            string skew = run.Face.SyntheticItalic ? "0.2126" : "0";
            string header = $"BT\n/{resource} {PdfSyntax.Num(size)} Tf\n{rgb(item.Color)} rg\n"
                + (run.Face.SyntheticBold ? $"{rgb(item.Color)} RG\n{PdfSyntax.Num(size * 0.03)} w\n2 Tr\n" : "");

            if (trueType && run.IsRightToLeft && run.Codes.Count > 1)
            {
                // Right-to-left runs are written one glyph at a time in LOGICAL order, each at its visual position,
                // so text extraction (copy, search) reads the text in the right order.
                int n = run.Codes.Count;
                var offsets = new double[n];
                double acc = 0;
                for (int v = 0; v < n; v++) { offsets[v] = acc; acc += run.Advances1000[v] * size / 1000.0; }

                Put(ms, header);
                for (int logical = 0; logical < n; logical++)
                {
                    int visual = n - 1 - logical;
                    Put(ms, $"1 0 {skew} 1 {PdfSyntax.Num(x(penMm + Units.PtToMm(offsets[visual])))} {PdfSyntax.Num(y(line.BaselineYMm))} Tm\n<{run.Codes[visual]:X4}> Tj\n");
                }
                Put(ms, "ET\n");
            }
            else
            {
                Put(ms, header);
                Put(ms, $"1 0 {skew} 1 {PdfSyntax.Num(x(penMm))} {PdfSyntax.Num(y(line.BaselineYMm))} Tm\n");
                if (trueType)
                    Put(ms, "<" + string.Concat(run.Codes.Select(c => c.ToString("X4"))) + "> Tj\n");
                else
                {
                    Put(ms, "(");
                    ms.Write(EscapeLiteral(run.Codes.Select(c => (byte)c).ToArray()));
                    Put(ms, ") Tj\n");
                }
                Put(ms, "ET\n");
            }
            if (shaped) Put(ms, "EMC\n");

            penMm += Units.PtToMm(widthPt);
        }
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
