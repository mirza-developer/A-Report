using AReport.Core.Tests.Support;
using AReport.Data;
using AReport.Fonts;
using AReport.Templates;

namespace AReport.Core.Tests;

public class TemplateEngineTests
{
    private readonly TemplateEngine _engine = TestKit.Templates();

    private string Render(string template, ReportData? data = null, int page = 1, int pages = 1, List<string>? warnings = null) =>
        _engine.Render(template, TestKit.Context(data, page, pages), warnings);

    [Fact]
    public void Plain_text_is_unchanged() => Assert.Equal("Hello", Render("Hello"));

    [Fact]
    public void Null_and_empty_templates_render_empty()
    {
        Assert.Equal("", _engine.Render(null, TestKit.Context()));
        Assert.Equal("", Render(""));
    }

    [Fact]
    public void Replaces_variables() =>
        Assert.Equal("Hello Ada!", Render("Hello {Name}!", new ReportData().AddVariable("Name", "Ada")));

    [Fact]
    public void Resolves_nested_paths_on_variables() =>
        Assert.Equal("Tehran", Render("{Customer.City}", new ReportData().AddVariable("Customer", new { City = "Tehran" })));

    [Fact]
    public void Resolves_the_first_row_of_a_data_source() =>
        Assert.Equal("Item 1", Render("{Items.Name}", new ReportData().AddDataSource("Items", TestKit.Rows(3))));

    [Fact]
    public void A_data_source_without_a_path_does_not_resolve()
    {
        var warnings = new List<string>();
        Assert.Equal("", Render("{Items}", new ReportData().AddDataSource("Items", TestKit.Rows(1)), warnings: warnings));
        Assert.Single(warnings);
    }

    [Fact]
    public void An_empty_data_source_resolves_to_blank_without_a_warning()
    {
        var warnings = new List<string>();
        Assert.Equal("[]", Render("[{Items.Name}]", new ReportData().AddDataSource("Items", Array.Empty<object>()), warnings: warnings));
        Assert.Empty(warnings);
    }

    [Fact]
    public void Built_ins_give_page_numbers_and_time()
    {
        Assert.Equal("Page 2 of 5", Render("Page {PageNumber} of {PageCount}", page: 2, pages: 5));
        Assert.Equal("2026-11-20", Render("{Now:yyyy-MM-dd}"));
    }

    [Fact]
    public void Built_ins_win_over_variables() =>
        Assert.Equal("1", Render("{PageNumber}", new ReportData().AddVariable("PageNumber", "x")));

    [Fact]
    public void Applies_formats_after_the_colon() =>
        Assert.Equal("Total 1,234.50", Render("Total {Amount:N2}", new ReportData().AddVariable("Amount", 1234.5m)));

    [Fact]
    public void Doubled_braces_are_literals() =>
        Assert.Equal("{Name} = Ada", Render("{{Name}} = {Name}", new ReportData().AddVariable("Name", "Ada")));

    [Fact]
    public void An_unclosed_brace_stays_literal() => Assert.Equal("a { b", Render("a { b"));

    [Fact]
    public void Empty_braces_stay_literal() => Assert.Equal("a {} b", Render("a {} b"));

    [Fact]
    public void Unresolved_placeholders_become_empty_and_warn_once()
    {
        var warnings = new DistinctWarnings();
        Assert.Equal("[]", _engine.Render("[{Nope}]", TestKit.Context(), warnings));
        _engine.Render("{Nope}", TestKit.Context(), warnings);
        Assert.Equal("Placeholder '{Nope}' could not be resolved.", Assert.Single(warnings));
    }

    [Fact]
    public void Strict_mode_throws_for_unresolved_placeholders()
    {
        var ex = Assert.Throws<ReportBindingException>(() => _engine.Render("{Nope}", TestKit.Context(strict: true)));
        Assert.Contains("Nope", ex.Message);
    }

    [Fact]
    public void A_null_value_renders_as_empty() =>
        Assert.Equal("[]", Render("[{X}]", new ReportData().AddVariable("X", null)));

    [Fact]
    public void Extracts_distinct_placeholder_paths_without_formats()
    {
        var found = _engine.ExtractPlaceholders("{A} {b.c:N2} {{literal}} {a} {}");
        Assert.Equal(new[] { "A", "b.c" }, found);
    }

    [Fact]
    public void Extract_handles_null() => Assert.Empty(_engine.ExtractPlaceholders(null));
}

public class WinAnsiTests
{
    [Fact]
    public void Ascii_and_latin1_map_directly()
    {
        Assert.Equal(new byte[] { 0x41, 0xE9 }, WinAnsi.Encode("A\u00E9"));
    }

    [Fact]
    public void Windows_1252_extras_map_to_their_code_points()
    {
        Assert.Equal(new byte[] { 0x80, 0x96, 0x97, 0x99 }, WinAnsi.Encode("\u20AC\u2013\u2014\u2122"));
    }

    [Fact]
    public void Unsupported_characters_become_question_marks()
    {
        Assert.Equal(new byte[] { (byte)'a', (byte)'?', (byte)'?' }, WinAnsi.Encode("a\u0633\u4E2D"));
        Assert.False(WinAnsi.IsFullySupported("a\u0633"));
        Assert.True(WinAnsi.IsFullySupported("Caf\u00E9 \u20AC"));
    }

    [Fact]
    public void Tabs_become_spaces_and_other_controls_are_unsupported()
    {
        Assert.Equal(new byte[] { 0x20 }, WinAnsi.Encode("\t"));
        Assert.False(WinAnsi.IsSupported('\u0001'));
    }
}

public class StandardFontsTests
{
    [Theory]
    [InlineData("Helvetica", StandardFonts.Family.Helvetica)]
    [InlineData("Arial", StandardFonts.Family.Helvetica)]
    [InlineData("Segoe UI", StandardFonts.Family.Helvetica)]
    [InlineData("Times New Roman", StandardFonts.Family.Times)]
    [InlineData("Georgia", StandardFonts.Family.Times)]
    [InlineData("Courier New", StandardFonts.Family.Courier)]
    [InlineData("Consolas", StandardFonts.Family.Courier)]
    [InlineData("monospace", StandardFonts.Family.Courier)]
    [InlineData("sans-serif", StandardFonts.Family.Helvetica)]
    [InlineData(null, StandardFonts.Family.Helvetica)]
    public void Resolves_families(string? name, StandardFonts.Family expected) =>
        Assert.Equal(expected, StandardFonts.ResolveFamily(name));

    [Theory]
    [InlineData("Helvetica", false, false, "Helvetica")]
    [InlineData("Helvetica", true, false, "Helvetica-Bold")]
    [InlineData("Helvetica", false, true, "Helvetica-Oblique")]
    [InlineData("Helvetica", true, true, "Helvetica-BoldOblique")]
    [InlineData("Times", false, false, "Times-Roman")]
    [InlineData("Times", true, true, "Times-BoldItalic")]
    [InlineData("Courier", true, false, "Courier-Bold")]
    [InlineData("Courier", false, true, "Courier-Oblique")]
    public void Names_the_pdf_font(string family, bool bold, bool italic, string expected) =>
        Assert.Equal(expected, StandardFonts.PdfName(new FontSpec(family, 10, bold, italic)));
}

public class StandardFontMetricsTests
{
    private readonly StandardFontMetrics _metrics = new();

    [Fact]
    public void Helvetica_widths_match_the_afm_table() =>
        Assert.Equal(22.78, _metrics.MeasureWidthPt("Hello", new FontSpec("Helvetica", 10, false, false)), 6);

    [Fact]
    public void Width_scales_with_font_size()
    {
        double at10 = _metrics.MeasureWidthPt("Hello", new FontSpec("Helvetica", 10, false, false));
        double at20 = _metrics.MeasureWidthPt("Hello", new FontSpec("Helvetica", 20, false, false));
        Assert.Equal(at10 * 2, at20, 9);
    }

    [Fact]
    public void Bold_is_wider_than_regular() =>
        Assert.True(_metrics.MeasureWidthPt("Total", new FontSpec("Helvetica", 10, true, false)) > _metrics.MeasureWidthPt("Total", new FontSpec("Helvetica", 10, false, false)));

    [Fact]
    public void Italic_has_the_same_widths_as_regular() =>
        Assert.Equal(_metrics.MeasureWidthPt("Total", new FontSpec("Helvetica", 10, false, false)), _metrics.MeasureWidthPt("Total", new FontSpec("Helvetica", 10, false, true)), 9);

    [Fact]
    public void Courier_is_monospaced() =>
        Assert.Equal(30, _metrics.MeasureWidthPt("iiiii", new FontSpec("Courier", 10, false, false)), 9);

    [Fact]
    public void Times_uses_its_own_table() =>
        Assert.Equal(12.22, _metrics.MeasureWidthPt("An", new FontSpec("Times", 10, false, false)), 6);

    [Fact]
    public void Non_ascii_characters_get_a_default_width() =>
        Assert.True(_metrics.MeasureWidthPt("\u00E9", new FontSpec("Helvetica", 10, false, false)) > 0);

    [Fact]
    public void Empty_text_has_no_width() =>
        Assert.Equal(0, _metrics.MeasureWidthPt("", new FontSpec("Helvetica", 10, false, false)));

    [Fact]
    public void Line_height_is_120_percent_of_the_size() =>
        Assert.Equal(12, _metrics.LineHeightPt(new FontSpec("Helvetica", 10, false, false)), 9);
}
