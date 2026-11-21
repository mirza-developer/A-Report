using AReport.Core.Tests.Support;
using AReport.Data;
using AReport.Fonts;
using AReport.Layout;
using AReport.Model;

namespace AReport.Core.Tests;

public class TextLayouterTests
{
    private readonly TextLayouter _layouter = new(new StandardFontMetrics());
    private static readonly FontSpec Courier = new("Courier", 10, false, false); // 6 pt per character

    private TextBlock Arrange(string text, double w = 100, double h = 20, HAlign ha = HAlign.Left, VAlign va = VAlign.Top, bool wrap = true) =>
        _layouter.Arrange(text, Courier, 10, 20, w, h, ha, va, wrap);

    [Fact]
    public void Left_top_text_starts_at_the_padding_and_ascent()
    {
        var line = Assert.Single(Arrange("abc").Lines);
        Assert.Equal(11, line.XMm, 6);
        Assert.Equal(20 + 1 + Units.PtToMm(9), line.BaselineYMm, 6);
    }

    [Fact]
    public void Centers_and_right_aligns_using_the_measured_width()
    {
        double width = Units.PtToMm(3 * 6);
        Assert.Equal(10 + (100 - width) / 2, Assert.Single(Arrange("abc", ha: HAlign.Center).Lines).XMm, 6);
        Assert.Equal(10 + 100 - 1 - width, Assert.Single(Arrange("abc", ha: HAlign.Right).Lines).XMm, 6);
    }

    [Fact]
    public void Middle_and_bottom_alignment_use_the_content_height()
    {
        double content = Units.PtToMm(12);
        double ascent = Units.PtToMm(9);
        Assert.Equal(20 + (20 - content) / 2 + ascent, Assert.Single(Arrange("abc", va: VAlign.Middle).Lines).BaselineYMm, 6);
        Assert.Equal(20 + 20 - 1 - content + ascent, Assert.Single(Arrange("abc", va: VAlign.Bottom).Lines).BaselineYMm, 6);
    }

    [Fact]
    public void Wraps_at_word_boundaries()
    {
        double box = Units.PtToMm(30.1) + 2; // inner width holds five characters
        var lines = Arrange("aa bb cc", w: box).Lines.Select(l => l.Text).ToList();
        Assert.Equal(new[] { "aa bb", "cc" }, lines);
    }

    [Fact]
    public void Breaks_words_that_are_wider_than_the_box()
    {
        double box = Units.PtToMm(18.1) + 2; // three characters
        Assert.Equal(new[] { "abc", "def", "gh" }, Arrange("abcdefgh", w: box).Lines.Select(l => l.Text));
    }

    [Fact]
    public void Honors_explicit_line_breaks_and_blank_lines()
    {
        Assert.Equal(new[] { "a", "", "b" }, Arrange("a\n\nb").Lines.Select(l => l.Text));
        Assert.Equal(new[] { "a", "b" }, Arrange("a\r\nb", wrap: false).Lines.Select(l => l.Text));
    }

    [Fact]
    public void Without_wrapping_a_long_line_stays_on_one_line()
    {
        double box = Units.PtToMm(18.1) + 2;
        Assert.Equal("abcdefgh", Assert.Single(Arrange("abcdefgh", w: box, wrap: false).Lines).Text);
    }

    [Fact]
    public void Reports_the_content_height()
    {
        Assert.Equal(3 * Units.PtToMm(12), Arrange("a\nb\nc").ContentHeightMm, 6);
    }

    [Fact]
    public void A_zero_width_box_does_not_loop_forever() =>
        Assert.NotEmpty(Arrange("abcdef", w: 0).Lines);
}

public class LayoutEngineTests
{
    private readonly LayoutEngine _engine = TestKit.Layout();

    private static IEnumerable<T> Items<T>(LaidOutPage page) where T : DrawItem => page.Items.OfType<T>();

    [Fact]
    public void Resolves_placeholders_in_text()
    {
        var doc = _engine.Layout(TestKit.Design(TestKit.Text("Hello {Name}")), new ReportData().AddVariable("Name", "Ada"));
        var text = Assert.Single(Items<TextItem>(Assert.Single(doc.Pages)));
        Assert.Equal("Hello Ada", Assert.Single(text.Lines).Text);
    }

    [Fact]
    public void Uses_the_design_name_or_the_given_title()
    {
        var design = TestKit.Design();
        Assert.Equal("Test", _engine.Layout(design, new ReportData()).Title);
        Assert.Equal("Q3", _engine.Layout(design, new ReportData(), "Q3").Title);
        Assert.Equal("Test", _engine.Layout(design, new ReportData(), "  ").Title);
    }

    [Fact]
    public void An_empty_design_still_has_one_page_of_the_right_size()
    {
        var doc = _engine.Layout(new ReportDesign { Page = PageSettings.A4Landscape() }, new ReportData());
        var page = Assert.Single(doc.Pages);
        Assert.Equal(297, page.WidthMm);
        Assert.Equal(210, page.HeightMm);
    }

    [Fact]
    public void Simple_shapes_become_draw_items()
    {
        var doc = _engine.Layout(TestKit.Design(
            new RectangleElement { X = 5, Y = 6, Width = 7, Height = 8, FillColor = "#FF0000", StrokeColor = null },
            new LineElement { X = 1, Y = 2, Width = 30, Height = 4, StrokeColor = "#00FF00", StrokeWidthMm = 0.5 }), new ReportData());
        var items = doc.Pages[0].Items;

        var rect = Assert.IsType<RectItem>(items[0]);
        Assert.Equal(new RgbColor(255, 0, 0), rect.Fill);
        Assert.Null(rect.Stroke);
        var line = Assert.IsType<LineItem>(items[1]);
        Assert.Equal(31, line.X2Mm);
        Assert.Equal(6, line.Y2Mm);
        Assert.Equal(0.5, line.WidthMm);
        Assert.Empty(doc.Warnings);
    }

    [Fact]
    public void Elements_keep_their_design_order()
    {
        var doc = _engine.Layout(TestKit.Design(new RectangleElement(), TestKit.Text("x"), new LineElement()), new ReportData());
        Assert.Equal(new[] { typeof(RectItem), typeof(TextItem), typeof(LineItem) }, doc.Pages[0].Items.Select(i => i.GetType()));
    }

    // ---------- tables ----------

    private static ReportDesign TableDesign(int columns = 2, params ReportElement[] extra)
    {
        var table = TestKit.Table("Items", 10, ("Name", "Name"), ("Qty", "Qty"));
        if (columns == 1) table.Columns.RemoveAt(1);
        return TestKit.Design([table, .. extra]);
    }

    [Fact]
    public void Splits_a_long_table_over_pages_and_repeats_the_header()
    {
        // A4: content ends at 287 mm; header 8, rows 7 -> floor((287-10-8)/7) = 38 rows per page.
        var data = new ReportData().AddDataSource("Items", TestKit.Rows(100));
        var doc = _engine.Layout(TableDesign(), data);

        Assert.Equal(3, doc.Pages.Count);
        Assert.Equal(2 + 38 * 2, Items<TextItem>(doc.Pages[0]).Count());
        Assert.Equal(2 + 38 * 2, Items<TextItem>(doc.Pages[1]).Count());
        Assert.Equal(2 + 24 * 2, Items<TextItem>(doc.Pages[2]).Count());

        foreach (var page in doc.Pages)
            Assert.Equal("Name", Items<TextItem>(page).First().Lines[0].Text);

        // Continuation pages start at the top margin.
        Assert.Equal(10, Items<RectItem>(doc.Pages[1]).First().YMm);
    }

    [Fact]
    public void Data_that_fits_exactly_does_not_add_an_empty_page()
    {
        var doc = _engine.Layout(TableDesign(), new ReportData().AddDataSource("Items", TestKit.Rows(38)));
        Assert.Single(doc.Pages);
        doc = _engine.Layout(TableDesign(), new ReportData().AddDataSource("Items", TestKit.Rows(39)));
        Assert.Equal(2, doc.Pages.Count);
    }

    [Fact]
    public void An_empty_data_source_draws_only_the_header()
    {
        var doc = _engine.Layout(TableDesign(), new ReportData().AddDataSource("Items", Array.Empty<object>()));
        Assert.Single(doc.Pages);
        Assert.Equal(2, Items<TextItem>(doc.Pages[0]).Count());
        Assert.Empty(doc.Warnings);
    }

    [Fact]
    public void A_missing_data_source_warns_and_draws_the_header()
    {
        var doc = _engine.Layout(TableDesign(), new ReportData());
        Assert.Equal("Data source 'Items' was not provided.", Assert.Single(doc.Warnings));
        Assert.Equal(2, Items<TextItem>(doc.Pages[0]).Count());
    }

    [Fact]
    public void Column_widths_follow_the_weights()
    {
        var table = TestKit.Table("Items", 10, ("A", "Name"), ("B", "Qty"));
        table.Width = 100;
        table.X = 20;
        table.Columns[0].Weight = 1;
        table.Columns[1].Weight = 3;
        var doc = _engine.Layout(TestKit.Design(table), new ReportData().AddDataSource("Items", TestKit.Rows(1)));
        var headerRects = Items<RectItem>(doc.Pages[0]).Take(2).ToList();

        Assert.Equal(25, headerRects[0].WidthMm, 9);
        Assert.Equal(75, headerRects[1].WidthMm, 9);
        Assert.Equal(45, headerRects[1].XMm, 9);
    }

    [Fact]
    public void Cells_use_formats_row_numbers_and_alignment()
    {
        var table = TestKit.Table("Items", 10, ("No", "#"), ("Amount", "Amount"));
        table.Columns[1].Format = "N2";
        table.Columns[1].Alignment = HAlign.Right;
        var rows = new List<object?> { new Dictionary<string, object?> { ["Amount"] = 1234.5m } };
        var doc = _engine.Layout(TestKit.Design(table), new ReportData().AddDataSource("Items", rows));
        var texts = Items<TextItem>(doc.Pages[0]).ToList();

        Assert.Equal("1", texts[2].Lines[0].Text);
        Assert.Equal("1,234.50", texts[3].Lines[0].Text);
        var cellRight = texts[3].XMm + texts[3].WidthMm;
        var textRight = texts[3].Lines[0].XMm + Units.PtToMm(new StandardFontMetrics().MeasureWidthPt("1,234.50", texts[3].Font));
        Assert.Equal(cellRight - 1.5, textRight, 6);
    }

    [Fact]
    public void Zebra_color_fills_every_second_row()
    {
        var table = TestKit.Table("Items", 10, ("N", "Name"));
        table.ZebraColor = "#EEEEEE";
        var doc = _engine.Layout(TestKit.Design(table), new ReportData().AddDataSource("Items", TestKit.Rows(3)));
        var rects = Items<RectItem>(doc.Pages[0]).Skip(1).ToList(); // skip header

        Assert.Null(rects[0].Fill);
        Assert.Equal(new RgbColor(0xEE, 0xEE, 0xEE), rects[1].Fill);
        Assert.Null(rects[2].Fill);
    }

    [Fact]
    public void A_missing_column_binding_warns_once()
    {
        var table = TestKit.Table("Items", 10, ("X", "Nope"));
        var doc = _engine.Layout(TestKit.Design(table), new ReportData().AddDataSource("Items", TestKit.Rows(5)));
        Assert.Equal("Table 'Table " + table.Id[..6] + "': column binding 'Nope' was not found.", Assert.Single(doc.Warnings));
    }

    [Fact]
    public void A_table_lower_on_the_page_fits_fewer_rows_on_the_first_page()
    {
        var table = TestKit.Table("Items", 200, ("N", "Name")); // (287-200-8)/7 = 11
        var doc = _engine.Layout(TestKit.Design(table), new ReportData().AddDataSource("Items", TestKit.Rows(20)));
        Assert.Equal(2, doc.Pages.Count);
        Assert.Equal(1 + 11, Items<TextItem>(doc.Pages[0]).Count());
        Assert.Equal(1 + 9, Items<TextItem>(doc.Pages[1]).Count());
    }

    // ---------- repeating elements and page numbers ----------

    [Fact]
    public void Only_repeating_elements_appear_on_every_page_and_page_numbers_are_filled_in()
    {
        var footer = TestKit.Text("Page {PageNumber} of {PageCount}", y: 280);
        footer.RepeatOnEveryPage = true;
        var title = TestKit.Text("Only first");
        var doc = _engine.Layout(TableDesign(2, footer, title), new ReportData().AddDataSource("Items", TestKit.Rows(100)));

        Assert.Equal(3, doc.Pages.Count);
        for (int p = 0; p < 3; p++)
        {
            var texts = Items<TextItem>(doc.Pages[p]).SelectMany(t => t.Lines).Select(l => l.Text).ToList();
            Assert.Contains($"Page {p + 1} of 3", texts);
            Assert.Equal(p == 0, texts.Contains("Only first"));
        }
    }

    // ---------- images ----------

    private static ImageElement ImageAt(string? key) => new() { ImageKey = key, X = 10, Y = 20, Width = 40, Height = 40 };

    [Fact]
    public void A_missing_image_warns_and_draws_nothing()
    {
        var doc = _engine.Layout(TestKit.Design(ImageAt("logo")), new ReportData());
        Assert.Empty(doc.Pages[0].Items);
        Assert.Equal("Image 'logo' was not provided.", Assert.Single(doc.Warnings));
    }

    [Fact]
    public void An_unreadable_image_reports_the_reason()
    {
        var data = new ReportData().AddImage("logo", [1, 2, 3, 4, 5]);
        var doc = _engine.Layout(TestKit.Design(ImageAt("logo")), data);
        Assert.Contains("Unsupported image format", Assert.Single(doc.Warnings));
    }

    [Fact]
    public void Contain_fit_keeps_the_aspect_ratio_and_centers()
    {
        var data = new ReportData().AddImage("logo", TestImages.Png(200, 100, 2, (_, _) => [0, 0, 0]));
        var item = Assert.Single(Items<ImageItem>(_engine.Layout(TestKit.Design(ImageAt("logo")), data).Pages[0]));

        Assert.Equal(10, item.XMm, 9);
        Assert.Equal(40, item.WidthMm, 9);
        Assert.Equal(20, item.HeightMm, 9);
        Assert.Equal(30, item.YMm, 9); // 20 + (40 - 20) / 2
    }

    [Fact]
    public void Contain_fit_handles_tall_images()
    {
        var data = new ReportData().AddImage("logo", TestImages.Jpeg(50, 100));
        var item = Assert.Single(Items<ImageItem>(_engine.Layout(TestKit.Design(ImageAt("logo")), data).Pages[0]));

        Assert.Equal(20, item.WidthMm, 9);
        Assert.Equal(40, item.HeightMm, 9);
        Assert.Equal(20, item.XMm, 9); // 10 + (40 - 20) / 2
    }

    [Fact]
    public void Stretch_fit_fills_the_box()
    {
        var element = ImageAt("logo");
        element.Fit = ImageFit.Stretch;
        var data = new ReportData().AddImage("logo", TestImages.Jpeg(200, 100));
        var item = Assert.Single(Items<ImageItem>(_engine.Layout(TestKit.Design(element), data).Pages[0]));
        Assert.Equal((10, 20, 40, 40), (item.XMm, item.YMm, item.WidthMm, item.HeightMm));
    }

    [Fact]
    public void Embedded_images_are_used_when_no_runtime_image_is_supplied()
    {
        var element = new ImageElement { EmbeddedImageBase64 = Convert.ToBase64String(TestImages.Jpeg(10, 10)), Width = 10, Height = 10 };
        var doc = _engine.Layout(TestKit.Design(element), new ReportData());
        Assert.Single(Items<ImageItem>(doc.Pages[0]));
        Assert.Empty(doc.Warnings);
    }

    [Fact]
    public void A_runtime_image_wins_over_the_embedded_one()
    {
        var element = ImageAt("logo");
        element.EmbeddedImageBase64 = Convert.ToBase64String(TestImages.Jpeg(10, 10));
        element.Fit = ImageFit.Stretch;
        var data = new ReportData().AddImage("logo", TestImages.Jpeg(77, 33));
        var item = Assert.Single(Items<ImageItem>(_engine.Layout(TestKit.Design(element), data).Pages[0]));
        Assert.Equal(77, item.Image.WidthPx);
    }

    [Fact]
    public void Missing_runtime_image_falls_back_to_the_embedded_one_without_warning()
    {
        var element = ImageAt("logo");
        element.EmbeddedImageBase64 = Convert.ToBase64String(TestImages.Jpeg(10, 10));
        var doc = _engine.Layout(TestKit.Design(element), new ReportData());
        Assert.Single(Items<ImageItem>(doc.Pages[0]));
        Assert.Empty(doc.Warnings);
    }

    [Fact]
    public void Broken_embedded_data_warns()
    {
        var doc = _engine.Layout(TestKit.Design(new ImageElement { EmbeddedImageBase64 = "!!!not base64!!!" }), new ReportData());
        Assert.Contains("invalid embedded data", Assert.Single(doc.Warnings));
    }

    // ---------- warnings ----------

    [Fact]
    public void An_invalid_color_warns_and_falls_back()
    {
        var text = TestKit.Text("x");
        text.Color = "teal";
        var doc = _engine.Layout(TestKit.Design(text), new ReportData());
        Assert.Equal(RgbColor.Black, Assert.Single(Items<TextItem>(doc.Pages[0])).Color);
        Assert.Contains("invalid colour 'teal'", Assert.Single(doc.Warnings));
    }

    [Fact]
    public void Text_that_does_not_fit_warns()
    {
        var text = TestKit.Text("a\nb\nc\nd", h: 5);
        var doc = _engine.Layout(TestKit.Design(text), new ReportData());
        Assert.Contains("does not fit", Assert.Single(doc.Warnings));
    }

    [Fact]
    public void Characters_outside_winansi_warn()
    {
        var doc = _engine.Layout(TestKit.Design(TestKit.Text("\u0633\u0644\u0627\u0645")), new ReportData());
        Assert.Contains("WinAnsi", Assert.Single(doc.Warnings));
    }

    [Fact]
    public void Repeated_elements_do_not_repeat_their_warnings()
    {
        var footer = TestKit.Text("{Missing}");
        footer.RepeatOnEveryPage = true;
        var doc = _engine.Layout(TableDesign(2, footer), new ReportData().AddDataSource("Items", TestKit.Rows(100)));
        Assert.Equal(3, doc.Pages.Count);
        Assert.Single(doc.Warnings);
    }

    [Fact]
    public void Strict_bindings_throw_from_layout()
    {
        var engine = TestKit.Layout(TestKit.Options(strict: true));
        Assert.Throws<ReportBindingException>(() => engine.Layout(TestKit.Design(TestKit.Text("{Missing}")), new ReportData()));
    }

    [Fact]
    public void Layout_validates_its_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Layout(null!, new ReportData()));
        Assert.Throws<ArgumentNullException>(() => _engine.Layout(TestKit.Design(), null!));
    }
}
