using System.Text;
using AReport.Core.Tests.Support;
using AReport.Data;
using AReport.Fonts;
using AReport.Layout;
using AReport.Model;
using AReport.Pdf;

namespace AReport.Core.Tests;

public class PdfRendererTests
{
    private static PdfRenderer Renderer(bool compress = false) => new(TestKit.Options(compress), TestKit.Time);

    private static byte[] Render(ReportDesign design, ReportData? data = null, bool compress = false, string? title = null)
    {
        var doc = TestKit.Layout().Layout(design, data ?? new ReportData(), title);
        return Renderer(compress).Render(doc);
    }

    private static PdfInspector Inspect(ReportDesign design, ReportData? data = null, bool compress = false, string? title = null) =>
        PdfInspector.Parse(Render(design, data, compress, title));

    private static LaidOutDocument Doc(params DrawItem[] items)
    {
        var page = new LaidOutPage(210, 297);
        page.Items.AddRange(items);
        return new LaidOutDocument("Doc", [page], []);
    }

    private static PdfInspector Raw(params DrawItem[] items) => PdfInspector.Parse(Renderer().Render(Doc(items)));

    private static TextItem Line(string text, FontSpec? font = null) =>
        new(10, 10, 100, 10, font ?? new FontSpec("Helvetica", 10, false, false), RgbColor.Black, [new TextLine(text, 11, 15)]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Produces_a_structurally_valid_pdf(bool compress)
    {
        var pdf = Inspect(TestKit.Design(TestKit.Text("Hello"), new RectangleElement()), compress: compress);
        pdf.VerifyStructure();
        Assert.Equal(1, pdf.PageCount);
    }

    [Fact]
    public void Writes_one_pdf_page_per_laid_out_page_with_the_right_media_box()
    {
        var design = TestKit.Design(TestKit.Table("Items", 10, ("N", "Name")));
        var pdf = Inspect(design, new ReportData().AddDataSource("Items", TestKit.Rows(100)));

        pdf.VerifyStructure();
        Assert.Equal(3, pdf.PageCount);
        Assert.Contains("/Count 3", pdf.Text);
        Assert.Contains("/MediaBox [0 0 595.276 841.89]", pdf.Text);
    }

    [Fact]
    public void Landscape_pages_swap_the_media_box()
    {
        var design = TestKit.Design();
        design.Page = PageSettings.A4Landscape();
        Assert.Contains("/MediaBox [0 0 841.89 595.276]", Inspect(design).Text);
    }

    [Fact]
    public void Draws_text_with_the_requested_font_and_color()
    {
        var text = TestKit.Text("Hello");
        text.Bold = true;
        text.Color = "#FF0000";
        var pdf = Inspect(TestKit.Design(text));

        Assert.True(pdf.HasText("Hello"));
        Assert.Contains("/BaseFont /Helvetica-Bold", pdf.Text);
        Assert.Contains("1 0 0 rg", pdf.Text);
        Assert.Contains("/F1 10 Tf", pdf.Text);
    }

    [Fact]
    public void Each_distinct_font_is_written_once()
    {
        var a = TestKit.Text("a");
        var b = TestKit.Text("b");
        b.Bold = true;
        var c = TestKit.Text("c");
        var pdf = Inspect(TestKit.Design(a, b, c));

        Assert.Equal(2, CountOf(pdf.Text, "/Type /Font"));
        Assert.Contains("/Encoding /WinAnsiEncoding", pdf.Text);
    }

    [Fact]
    public void Text_is_clipped_to_its_box()
    {
        var pdf = Inspect(TestKit.Design(TestKit.Text("x")));
        Assert.Contains(" re W n\n", pdf.Text);
    }

    [Fact]
    public void Escapes_parentheses_and_backslashes()
    {
        Assert.True(Raw(Line("a(b)c\\d")).Text.Contains("(a\\(b\\)c\\\\d) Tj"));
    }

    [Fact]
    public void Encodes_latin_text_as_winansi_bytes()
    {
        var pdf = Raw(Line("caf\u00E9 \u20AC"));
        Assert.Contains("(caf\u00E9 \u0080) Tj", pdf.Text); // Latin-1 view: é = E9, euro = 80
    }

    [Fact]
    public void Characters_outside_winansi_are_drawn_as_question_marks() =>
        Assert.True(Raw(Line("a\u0633b")).HasText("a?b"));

    [Fact]
    public void Blank_lines_are_not_drawn()
    {
        var pdf = Raw(new TextItem(10, 10, 100, 10, new FontSpec("Helvetica", 10, false, false), RgbColor.Black, [new TextLine("", 11, 15)]));
        Assert.DoesNotContain("BT", pdf.Text);
    }

    [Fact]
    public void Rectangles_use_the_right_paint_operator()
    {
        var red = new RgbColor(255, 0, 0);
        Assert.Contains("\nB\n", Raw(new RectItem(10, 10, 20, 20, red, RgbColor.Black, 0.3)).Text);
        Assert.Contains("\nf\n", Raw(new RectItem(10, 10, 20, 20, red, null, 0)).Text);
        Assert.Contains("\nS\n", Raw(new RectItem(10, 10, 20, 20, null, red, 0.3)).Text);
        Assert.DoesNotContain(" re\n", Raw(new RectItem(10, 10, 20, 20, null, null, 0)).Text);
    }

    [Fact]
    public void Coordinates_are_flipped_to_the_pdf_origin_at_the_bottom_left()
    {
        // 10 mm from the top of a 297 mm page, 20 mm tall: bottom edge at (297 - 30) mm = 756.85 pt.
        var pdf = Raw(new RectItem(10, 10, 20, 20, RgbColor.Black, null, 0));
        Assert.Contains("28.346 756.85 56.693 56.693 re", pdf.Text);
    }

    [Fact]
    public void Lines_are_stroked()
    {
        var pdf = Raw(new LineItem(10, 10, 110, 10, new RgbColor(0, 0, 255), 0.5));
        Assert.Contains("0 0 1 RG", pdf.Text);
        Assert.Contains(" m ", pdf.Text);
        Assert.Contains(" l S", pdf.Text);
    }

    [Fact]
    public void Document_info_has_title_producer_and_creation_date()
    {
        var pdf = Inspect(TestKit.Design(), title: "Invoice (Q3)");
        Assert.Contains("/Title (Invoice \\(Q3\\))", pdf.Text);
        Assert.Contains("/Producer (A-Report)", pdf.Text);
        Assert.Contains("/CreationDate (D:20261120083000Z)", pdf.Text);
    }

    [Fact]
    public void Unicode_titles_are_stored_as_utf16()
    {
        var pdf = Inspect(TestKit.Design(), title: "\u06AF\u0632\u0627\u0631\u0634");
        Assert.Contains("/Title <FEFF06AF0632062706310634>", pdf.Text);
    }

    [Fact]
    public void Output_is_deterministic_for_the_same_input_and_clock()
    {
        var design = TestKit.Design(TestKit.Text("Hello {Name}"));
        var data = new ReportData().AddVariable("Name", "Ada");
        Assert.Equal(Render(design, data), Render(design, data));
    }

    [Fact]
    public void Compression_shrinks_large_documents_and_hides_the_plain_text()
    {
        var design = TestKit.Design(TestKit.Table("Items", 10, ("N", "Name"), ("Q", "Qty")));
        var data = new ReportData().AddDataSource("Items", TestKit.Rows(200));
        var plain = Render(design, data, compress: false);
        var packed = Render(design, data, compress: true);

        Assert.True(packed.Length < plain.Length / 2, $"{packed.Length} vs {plain.Length}");
        Assert.Contains("/Filter /FlateDecode", Encoding.Latin1.GetString(packed));
        Assert.DoesNotContain("(Item 7) Tj", Encoding.Latin1.GetString(packed));
    }

    // ---------- images ----------

    private static PdfInspector WithImage(byte[] image, bool compress = false) =>
        Inspect(TestKit.Design(new ImageElement { ImageKey = "i", Width = 20, Height = 20 }), new ReportData().AddImage("i", image), compress);

    [Fact]
    public void Jpeg_is_embedded_without_re_encoding()
    {
        var jpeg = TestImages.Jpeg(30, 20);
        var pdf = WithImage(jpeg);

        pdf.VerifyStructure();
        Assert.Contains("/Subtype /Image /Width 30 /Height 20", pdf.Text);
        Assert.Contains("/Filter /DCTDecode", pdf.Text);
        Assert.Contains("/ColorSpace /DeviceRGB", pdf.Text);
        Assert.Contains("/Im1 Do", pdf.Text);
        Assert.Contains("/XObject << /Im1 ", pdf.Text);
        Assert.True(IndexOf(pdf.Bytes, jpeg) > 0, "the JPEG bytes should appear unchanged in the file");
    }

    [Fact]
    public void Gray_jpeg_uses_the_gray_color_space() =>
        Assert.Contains("/ColorSpace /DeviceGray", WithImage(TestImages.Jpeg(4, 4, 1)).Text);

    [Fact]
    public void Rgb_png_is_embedded_with_a_png_predictor()
    {
        var pdf = WithImage(TestImages.Png(5, 4, 2, (_, _) => [1, 2, 3]));
        pdf.VerifyStructure();
        Assert.Contains("/Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns 5", pdf.Text);
        Assert.Contains("/ColorSpace /DeviceRGB", pdf.Text);
    }

    [Fact]
    public void Gray_png_uses_one_color_per_pixel()
    {
        var pdf = WithImage(TestImages.Png(3, 3, 0, (_, _) => [200]));
        Assert.Contains("/ColorSpace /DeviceGray", pdf.Text);
        Assert.Contains("/Colors 1", pdf.Text);
    }

    [Fact]
    public void Palette_png_is_written_as_an_indexed_color_space()
    {
        var pdf = WithImage(TestImages.Png(2, 1, 3, (x, _) => [(byte)x], palette: [255, 0, 0, 0, 255, 0]));
        Assert.Contains("/ColorSpace [/Indexed /DeviceRGB 1 <FF000000FF00>]", pdf.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rgba_png_gets_a_soft_mask_for_its_alpha_channel(bool compress)
    {
        var pdf = WithImage(TestImages.Png(4, 4, 6, (x, y) => [10, 20, 30, (byte)(x * 60)], filter: 1), compress);
        pdf.VerifyStructure();
        Assert.Contains("/SMask ", pdf.Text);
        Assert.Equal(2, CountOf(pdf.Text, "/Subtype /Image"));
    }

    [Fact]
    public void The_same_image_used_twice_is_stored_once()
    {
        var image = TestImages.Jpeg(10, 10);
        var design = TestKit.Design(
            new ImageElement { ImageKey = "i", X = 10, Y = 10 },
            new ImageElement { ImageKey = "i", X = 10, Y = 100 });
        var pdf = Inspect(design, new ReportData().AddImage("i", image));

        Assert.Equal(1, CountOf(pdf.Text, "/Subtype /Image"));
        Assert.Equal(2, CountOf(pdf.Text, "/Im1 Do"));
    }

    // ---------- validator ----------

    [Fact]
    public void Output_validator_accepts_real_output() =>
        new PdfOutputValidator().Validate(Render(TestKit.Design(TestKit.Text("x"))));

    [Fact]
    public void Output_validator_rejects_empty_content() =>
        Assert.Contains("empty", Assert.Throws<ReportRenderException>(() => new PdfOutputValidator().Validate([])).Message);

    [Fact]
    public void Output_validator_rejects_a_wrong_header() =>
        Assert.Contains("header", Assert.Throws<ReportRenderException>(() => new PdfOutputValidator().Validate("<html>nope</html>"u8.ToArray())).Message);

    [Fact]
    public void Output_validator_rejects_a_truncated_file() =>
        Assert.Contains("end-of-file", Assert.Throws<ReportRenderException>(() => new PdfOutputValidator().Validate("%PDF-1.4\nstuff"u8.ToArray())).Message);

    [Fact]
    public void Content_type_and_extension_describe_pdf()
    {
        Assert.Equal("application/pdf", Renderer().ContentType);
        Assert.Equal(".pdf", Renderer().FileExtension);
    }

    // ---------- syntax helpers ----------

    [Theory]
    [InlineData(1.23456, "1.235")]
    [InlineData(-0.0001, "0")]
    [InlineData(10.0, "10")]
    [InlineData(0.5, "0.5")]
    public void Numbers_are_written_compactly_and_culture_independent(double value, string expected) =>
        Assert.Equal(expected, PdfSyntax.Num(value));

    [Fact]
    public void Number_formatting_ignores_the_current_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var comma = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            System.Globalization.CultureInfo.CurrentCulture = comma;
            Assert.Equal("1.5", PdfSyntax.Num(1.5));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0) { count++; index += needle.Length; }
        return count;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }
}
