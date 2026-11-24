using AReport.Core.Tests.Support;
using AReport.Fonts;
using AReport.Layout;
using AReport.Model;

namespace AReport.Core.Tests;

public class BidiTests
{
    private const string Salam = "سلام";   // "hello": four Arabic-script letters

    private static string Visual(string text, TextDirection direction = TextDirection.Auto)
    {
        var cps = text.EnumerateRunes().Select(r => r.Value).ToList();
        var levels = Bidi.ResolveLevels(cps, direction, out int baseLevel);
        var order = Bidi.VisualOrder(cps, levels, 0, cps.Count, baseLevel);
        return string.Concat(order.Select(i => char.ConvertFromUtf32(levels[i] % 2 == 1 ? Bidi.Mirror(cps[i]) : cps[i])));
    }

    private static int[] Levels(string text, TextDirection direction, out int baseLevel) =>
        Bidi.ResolveLevels(text.EnumerateRunes().Select(r => r.Value).ToList(), direction, out baseLevel);

    [Theory]
    [InlineData('a', "L")]
    [InlineData(0x05D0, "R")]
    [InlineData(0x0628, "AL")]
    [InlineData('1', "EN")]
    [InlineData(0x0661, "AN")]
    [InlineData(' ', "WS")]
    [InlineData(0x200C, "BN")]
    [InlineData(0x4E2D, "L")]
    [InlineData(0x1F600, "L")]
    public void Classifies_characters(int codePoint, string expected) => Assert.Equal(expected, Bidi.ClassOf(codePoint).ToString());

    [Fact]
    public void Latin_text_is_untouched() => Assert.Equal("Hello, World 123", Visual("Hello, World 123"));

    [Fact]
    public void Persian_text_is_reversed_into_visual_order() => Assert.Equal("مالس", Visual(Salam));

    [Fact]
    public void The_first_strong_character_sets_the_paragraph_direction()
    {
        Levels(Salam + " abc", TextDirection.Auto, out int rtl);
        Levels("abc " + Salam, TextDirection.Auto, out int ltr);
        Levels("123 !", TextDirection.Auto, out int none);
        Assert.Equal((1, 0, 0), (rtl, ltr, none));
    }

    [Fact]
    public void An_explicit_direction_overrides_detection()
    {
        Levels("abc", TextDirection.RightToLeft, out int forcedRtl);
        Levels(Salam, TextDirection.LeftToRight, out int forcedLtr);
        Assert.Equal((1, 0), (forcedRtl, forcedLtr));
    }

    [Fact]
    public void Latin_words_inside_persian_keep_their_internal_order() =>
        Assert.Equal("abc مالس", Visual(Salam + " abc"));

    [Fact]
    public void Persian_words_inside_latin_are_reversed_in_place() =>
        Assert.Equal("abc مالس", Visual("abc " + Salam));

    [Fact]
    public void Numbers_keep_their_order_inside_right_to_left_text()
    {
        Assert.Equal("123 مالس", Visual(Salam + " 123"));
        var levels = Levels(Salam + " 123", TextDirection.Auto, out _);
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 2, 2, 2 }, levels);
    }

    [Fact]
    public void A_separator_inside_a_number_keeps_it_together_after_arabic_letters()
    {
        // W2 turns the digits into Arabic numbers after an Arabic letter and W4 lets the comma join them
        var levels = Levels(Salam + " 1,234 $5", TextDirection.Auto, out _);
        Assert.Equal(new[] { 2, 2, 2, 2, 2 }, levels[5..10]);    // 1,234
        Assert.Equal(1, levels[11]);                             // the currency sign is a neutral between two right-to-left items
        Assert.Equal(2, levels[12]);                             // 5
    }

    [Fact]
    public void Numbers_in_left_to_right_text_are_ordinary_left_to_right_text()
    {
        var levels = Levels("abc 1,234", TextDirection.Auto, out _);
        Assert.All(levels, l => Assert.Equal(0, l));   // W7: European numbers after L become L
    }

    [Fact]
    public void Brackets_are_mirrored_in_right_to_left_runs() => Assert.Equal("(مالس)", Visual("(" + Salam + ")"));

    [Fact]
    public void Brackets_around_latin_text_inside_persian_enclose_it_correctly()
    {
        // N0: the pair takes the direction of its contents when that matches the paragraph, else the surrounding context
        Assert.Equal("(abc) مالس", Visual(Salam + " (abc)"));
    }

    [Fact]
    public void Trailing_spaces_return_to_the_paragraph_direction()
    {
        // L1: a trailing space on a left-to-right line must not jump to the left of the Persian word
        Assert.Equal("abc مالس ", Visual("abc " + Salam + " "));
    }

    [Fact]
    public void Joiner_controls_do_not_disturb_the_surrounding_levels()
    {
        var levels = Levels("ب\u200Cب", TextDirection.Auto, out _);
        Assert.Equal(new[] { 1, 1, 1 }, levels);
    }

    [Fact]
    public void Empty_text_is_fine()
    {
        Assert.Equal("", Visual(""));
        Assert.Empty(Levels("", TextDirection.Auto, out int baseLevel));
        Assert.Equal(0, baseLevel);
    }

    [Fact]
    public void Mirror_only_changes_brackets()
    {
        Assert.Equal(')', Bidi.Mirror('('));
        Assert.Equal('[', Bidi.Mirror(']'));
        Assert.Equal('«', Bidi.Mirror('»'));
        Assert.Equal('a', Bidi.Mirror('a'));
    }
}

public class ArabicShaperTests
{
    private static List<int> Glyphs(string text) =>
        ArabicShaper.Shape(text.EnumerateRunes().Select(r => r.Value).ToList()).Select(u => u.GlyphCodePoint).ToList();

    [Fact]
    public void Joins_letters_and_forms_the_lam_alef_ligature()
    {
        // seen (initial) + lam-alef (final ligature) + meem (isolated: alef does not join forward)
        Assert.Equal(new[] { 0xFEB3, 0xFEFC, 0xFEE1 }, Glyphs("سلام"));
    }

    [Fact]
    public void A_dual_joining_letter_has_isolated_initial_medial_and_final_forms()
    {
        Assert.Equal(new[] { 0xFE8F }, Glyphs("ب"));
        Assert.Equal(new[] { 0xFE91, 0xFE90 }, Glyphs("بب"));
        Assert.Equal(new[] { 0xFE91, 0xFE92, 0xFE90 }, Glyphs("ببب"));
    }

    [Theory]
    [InlineData("پ", 0xFB56)]
    [InlineData("چ", 0xFB7A)]
    [InlineData("ژ", 0xFB8A)]
    [InlineData("گ", 0xFB92)]
    [InlineData("ک", 0xFB8E)]
    [InlineData("ی", 0xFBFC)]
    public void Persian_specific_letters_have_their_own_forms(string letter, int isolated) =>
        Assert.Equal(new[] { isolated }, Glyphs(letter));

    [Fact]
    public void Right_joining_letters_never_join_the_next_letter() =>
        Assert.Equal(new[] { 0xFE8D, 0xFE8F }, Glyphs("اب"));   // alef isolated, beh isolated

    [Fact]
    public void The_zero_width_non_joiner_breaks_a_join()
    {
        Assert.Equal(new[] { 0xFE8F, 0x200C, 0xFE8F }, Glyphs("ب\u200Cب"));
        Assert.Equal(new[] { 0xFE91, 0xFE90 }, Glyphs("بب"));
    }

    [Fact]
    public void Marks_are_transparent_to_joining() =>
        Assert.Equal(new[] { 0xFE91, 0x064E, 0xFE90 }, Glyphs("بَب"));   // beh + fatha + beh

    [Fact]
    public void Lam_alef_stands_alone_or_joins_what_comes_before()
    {
        Assert.Equal(new[] { 0xFEFB }, Glyphs("لا"));
        Assert.Equal(new[] { 0xFE91, 0xFEFC }, Glyphs("بلا"));
    }

    [Fact]
    public void Other_scripts_and_digits_pass_through()
    {
        Assert.Equal(new[] { 'a', '1', ' ', 0x4E2D }, Glyphs("a1 中"));
        Assert.Equal(new[] { 0x06F1 }, Glyphs("۱"));
    }

    [Fact]
    public void Ligatures_report_how_many_characters_they_cover()
    {
        var units = ArabicShaper.Shape("سلام".EnumerateRunes().Select(r => r.Value).ToList());
        Assert.Equal(new[] { (0, 1), (1, 2), (3, 1) }, units.Select(u => (u.Start, u.Length)));
    }

    [Fact]
    public void Script_and_invisible_character_classification()
    {
        Assert.True(ArabicShaper.IsArabicScript(0x0628));
        Assert.True(ArabicShaper.IsArabicScript(0xFEB3));
        Assert.False(ArabicShaper.IsArabicScript('a'));
        Assert.True(ArabicShaper.IsInvisible(0x200C));
        Assert.True(ArabicShaper.IsInvisible(0x200F));
        Assert.False(ArabicShaper.IsInvisible(' '));
        Assert.Equal(Script.Cjk, UnicodeScripts.Of('中'));
        Assert.Equal(Script.Arabic, UnicodeScripts.Of(0x0628));
        Assert.Equal(Script.Latin, UnicodeScripts.Of('a'));
        Assert.Equal(Script.Common, UnicodeScripts.Of('1'));
        Assert.True(UnicodeScripts.IsClosingPunctuation('。'));
    }
}

public class TextShaperTests
{
    private readonly TextShaper _shaper = new(TestFonts.Fonts());

    private static readonly FontSpec Sans = new("AReport Test Sans", 10, false, false);
    private static readonly FontSpec Helvetica = new("Helvetica", 10, false, false);
    private static readonly FontSpec Cjk = new("AReport Test CJK", 10, false, false);

    private IReadOnlyList<ShapedLine> Lay(string text, FontSpec? font = null, double width = double.PositiveInfinity,
        TextDirection direction = TextDirection.Auto, DigitStyle digits = DigitStyle.Latin, List<string>? warnings = null) =>
        _shaper.Layout(text, font ?? Sans, direction, digits, width, warnings is null ? null : warnings.Add);

    [Fact]
    public void Latin_text_in_helvetica_is_one_standard_run_with_afm_widths()
    {
        var line = Assert.Single(Lay("Hello", Helvetica));
        var run = Assert.Single(line.Runs);
        Assert.True(run.Face.IsStandard);
        Assert.Equal("Hello", line.Text);
        Assert.Equal(22.78, line.WidthPt, 6);
        Assert.False(line.IsRightToLeft);
    }

    [Fact]
    public void Persian_text_becomes_one_right_to_left_run_of_embedded_glyphs()
    {
        var line = Assert.Single(Lay("سلام دنیا"));
        Assert.True(line.IsRightToLeft);
        Assert.Equal("سلام دنیا", line.Text);                  // logical text is kept for ActualText / search
        Assert.All(line.Runs, r => Assert.False(r.Face.IsStandard));
        Assert.All(line.Runs, r => Assert.True(r.IsRightToLeft));
        Assert.True(line.WidthPt > 20);
    }

    [Fact]
    public void Glyphs_come_in_visual_order_with_presentation_forms()
    {
        var font = TestFonts.Library.Find("AReport Test Sans", false, false)!.Font;
        var run = Assert.Single(Assert.Single(Lay("ببب")).Runs);
        // visual (left to right) = final, medial, initial
        Assert.Equal(new[] { font.GlyphFor(0xFE90), font.GlyphFor(0xFE92), font.GlyphFor(0xFE91) }, run.Codes.Select(c => (ushort)c));
    }

    [Fact]
    public void Mixed_text_splits_into_runs_by_font_and_direction()
    {
        var line = Assert.Single(Lay("Hello سلام", Helvetica));
        Assert.Equal(2, line.Runs.Count);
        Assert.True(line.Runs[0].Face.IsStandard);
        Assert.Equal(0, line.Runs[0].Level);
        Assert.False(line.Runs[1].Face.IsStandard);
        Assert.Equal(1, line.Runs[1].Level);
        Assert.Equal("Hello سلام", line.Text);
    }

    [Fact]
    public void Brackets_are_drawn_mirrored_in_right_to_left_text()
    {
        var font = TestFonts.Library.Find("AReport Test Sans", false, false)!.Font;
        var run = Assert.Single(Assert.Single(Lay("(ب)")).Runs);
        Assert.Equal((int)font.GlyphFor('('), run.Codes[0]);   // the logical closing bracket is leftmost and drawn mirrored
        Assert.Equal((int)font.GlyphFor(')'), run.Codes[^1]);
    }

    [Theory]
    [InlineData(DigitStyle.Persian, "۱۲۳")]
    [InlineData(DigitStyle.Arabic, "١٢٣")]
    [InlineData(DigitStyle.Latin, "123")]
    public void Digits_can_be_drawn_in_other_shapes(DigitStyle style, string expected)
    {
        var line = Assert.Single(Lay("123", Helvetica, digits: style));
        Assert.Equal(expected, line.Text);
        Assert.Equal(style == DigitStyle.Latin, line.Runs[0].Face.IsStandard);
    }

    [Fact]
    public void Persian_text_wraps_at_spaces_and_each_line_is_laid_out_on_its_own()
    {
        var lines = Lay("سلام دنیا من اینجا هستم", Sans, width: 45);
        Assert.True(lines.Count >= 2, $"{lines.Count} lines");
        Assert.All(lines, l => Assert.True(l.WidthPt <= 45 + 1e-6));
        Assert.Equal("سلام دنیا من اینجا هستم", string.Join(" ", lines.Select(l => l.Text)));
        Assert.All(lines, l => Assert.True(l.IsRightToLeft));
    }

    [Fact]
    public void Chinese_text_breaks_between_any_two_characters()
    {
        // each character is 1000/1000 em = 10 pt wide; 25 pt fits two
        var lines = Lay("中文报告发票", Cjk, width: 25);
        Assert.Equal(new[] { "中文", "报告", "发票" }, lines.Select(l => l.Text));
    }

    [Fact]
    public void Chinese_closing_punctuation_never_starts_a_line()
    {
        var lines = Lay("中文报告，发票金额。合计", Cjk, width: 25);
        Assert.All(lines, l => Assert.False("，。：".Contains(l.Text[0]), $"line starts with closing punctuation: {l.Text}"));
        Assert.Equal("中文报告，发票金额。合计", string.Concat(lines.Select(l => l.Text)));
    }

    [Fact]
    public void Long_words_break_by_character_and_always_make_progress()
    {
        var lines = Lay("abcdefgh", new FontSpec("Courier", 10, false, false), width: 18);
        Assert.Equal(new[] { "abc", "def", "gh" }, lines.Select(l => l.Text));
        Assert.NotEmpty(Lay("abcdef", Helvetica, width: 0));
    }

    [Fact]
    public void Trailing_spaces_are_dropped_at_a_wrap_but_leading_ones_stay()
    {
        var lines = Lay("aa bb cc", new FontSpec("Courier", 10, false, false), width: 30.1);
        Assert.Equal(new[] { "aa bb", "cc" }, lines.Select(l => l.Text));
        Assert.Equal(" x", Assert.Single(Lay(" x", Helvetica)).Text);
    }

    [Fact]
    public void Zero_width_joiners_are_not_drawn()
    {
        var run = Assert.Single(Assert.Single(Lay("ب\u200Cب")).Runs);
        Assert.Equal(2, run.Codes.Count);
    }

    [Fact]
    public void Empty_text_gives_one_empty_line()
    {
        var line = Assert.Single(Lay(""));
        Assert.Empty(line.Runs);
        Assert.Equal(0, line.WidthPt);
    }

    [Fact]
    public void Characters_no_font_has_are_reported_once_and_drawn_as_question_marks()
    {
        var warnings = new List<string>();
        var line = Assert.Single(Lay("a😀b😁", Helvetica, warnings: warnings));
        Assert.Contains("2 character(s)", Assert.Single(warnings));
        Assert.Contains(line.Runs, r => r.Codes.Contains('?'));
    }

    [Fact]
    public void Synthetic_bold_and_italic_come_from_the_face_not_the_glyphs()
    {
        var face = _shaper.Layout("中", new FontSpec("AReport Test CJK", 10, true, true), TextDirection.Auto, DigitStyle.Latin, double.PositiveInfinity)[0].Runs[0].Face;
        Assert.True(face.SyntheticBold);
        Assert.True(face.SyntheticItalic);
    }
}

public class TextLayouterUnicodeTests
{
    private readonly TextLayouter _layouter = new(TestFonts.Fonts());
    private static readonly FontSpec Sans = new("AReport Test Sans", 10, false, false);

    [Fact]
    public void Right_aligned_persian_text_ends_at_the_right_padding_and_carries_its_runs()
    {
        var block = _layouter.Arrange("سلام", Sans, 10, 20, 100, 10, HAlign.Right, VAlign.Top, true);
        var line = Assert.Single(block.Lines);
        Assert.NotNull(line.Runs);
        Assert.Equal(10 + 100 - 1, line.XMm + line.WidthMm, 6);
        Assert.Equal("سلام", line.Text);
    }

    [Fact]
    public void Direction_and_digits_are_passed_through()
    {
        var block = _layouter.Arrange("12", Sans, 0, 0, 50, 10, HAlign.Left, VAlign.Top, true, 1, TextDirection.RightToLeft, DigitStyle.Persian);
        Assert.Equal("۱۲", Assert.Single(block.Lines).Text);
    }

    [Fact]
    public void Warnings_from_the_font_system_reach_the_caller()
    {
        var warnings = new List<string>();
        new TextLayouter(FontSystem.StandardOnly).Arrange("سلام", Sans, 0, 0, 50, 10, HAlign.Left, VAlign.Top, true, 1, TextDirection.Auto, DigitStyle.Latin, warnings.Add);
        Assert.Single(warnings);
    }
}
