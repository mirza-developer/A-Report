using AReport.Core.Tests.Support;
using AReport.Fonts;

namespace AReport.Core.Tests;

public class TrueTypeFontTests
{
    private static byte[] Bytes(string file) => File.ReadAllBytes(Path.Combine(TestFonts.Directory, file));

    private static TrueTypeFont Sans() => TrueTypeFont.Load(Bytes("AReportTestSans-Regular.ttf"));

    [Fact]
    public void Reads_names_metrics_and_outline_type()
    {
        var font = Sans();
        Assert.Equal("AReport Test Sans", font.Names.Family);
        Assert.Equal("Regular", font.Names.Subfamily);
        Assert.False(font.Names.Bold);
        Assert.True(font.HasGlyphOutlines);
        Assert.Equal(2048, font.UnitsPerEm);
        Assert.True(font.Ascender > 0 && font.Descender < 0);
        Assert.True(font.NumGlyphs > 100);
    }

    [Fact]
    public void Bold_face_is_marked_bold() => Assert.True(TrueTypeFont.Load(Bytes("AReportTestSans-Bold.ttf")).Names.Bold);

    [Fact]
    public void Maps_latin_and_persian_code_points_to_glyphs()
    {
        var font = Sans();
        Assert.True(font.Covers('A'));
        Assert.True(font.Covers(0x067E));   // peh
        Assert.True(font.Covers(0xFB56));   // peh, isolated presentation form
        Assert.True(font.Covers(0xFEB3));   // seen, initial form
        Assert.True(font.Covers(0x06F1));   // Persian digit one
        Assert.False(font.Covers(0x4E2D));  // a Chinese character is not in this font
        Assert.Equal(0, font.GlyphFor(0x4E2D));
    }

    [Fact]
    public void Advances_are_proportional_and_scale_to_one_thousand_units()
    {
        var font = Sans();
        double w = font.Advance1000(font.GlyphFor('W')), i = font.Advance1000(font.GlyphFor('i'));
        Assert.True(w > i * 2, $"W ({w}) should be much wider than i ({i})");
        Assert.True(font.Advance1000(font.GlyphFor(' ')) > 0);
    }

    [Fact]
    public void Rejects_data_that_is_not_a_font()
    {
        Assert.Throws<InvalidDataException>(() => TrueTypeFont.Load(new byte[11]));
        Assert.ThrowsAny<Exception>(() => TrueTypeFont.Load(new byte[64]));
    }

    [Fact]
    public void Loads_a_face_from_a_font_collection()
    {
        var collection = TestFonts.ToCollection(Bytes("AReportTestSans-Regular.ttf"));
        Assert.Equal(1, TrueTypeFont.FaceCount(collection));
        var font = TrueTypeFont.Load(collection, 0);
        Assert.Equal("AReport Test Sans", font.Names.Family);
        Assert.True(font.Covers(0x0628));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrueTypeFont.Load(collection, 1));
    }

    [Fact]
    public void Reads_names_without_loading_the_whole_file()
    {
        var names = Assert.Single(TrueTypeFont.ReadNames(Path.Combine(TestFonts.Directory, "AReportTestSans-Bold.ttf")));
        Assert.Equal("AReport Test Sans", names.Family);
        Assert.True(names.Bold);
        Assert.True(names.HasGlyphOutlines);
    }

    [Fact]
    public void The_synthetic_cjk_font_covers_its_characters()
    {
        var font = TrueTypeFont.Load(Bytes("AReportTestCJK-Regular.ttf"));
        Assert.Equal("AReport Test CJK", font.Names.Family);
        Assert.True(font.Covers('中') && font.Covers('文') && font.Covers('，'));
        Assert.False(font.Covers('A'));
        Assert.Equal(1000, font.Advance1000(font.GlyphFor('中')));
    }
}

public class TrueTypeSubsetterTests
{
    private static TrueTypeFont Sans() => TrueTypeFont.Load(File.ReadAllBytes(Path.Combine(TestFonts.Directory, "AReportTestSans-Regular.ttf")));

    [Fact]
    public void A_subset_is_much_smaller_but_keeps_glyph_ids_and_metrics()
    {
        var font = Sans();
        int a = font.GlyphFor('A'), b = font.GlyphFor('B');
        var subset = TrueTypeSubsetter.Subset(font, [a, b]);
        var loaded = TrueTypeFont.Load(subset);

        Assert.True(subset.Length < font.RawData.Length / 2, $"{subset.Length} vs {font.RawData.Length}");
        Assert.Equal(font.NumGlyphs, loaded.NumGlyphs);
        Assert.Equal(font.UnitsPerEm, loaded.UnitsPerEm);
        Assert.Equal(font.AdvanceUnits(a), loaded.AdvanceUnits(a));
        Assert.True(loaded.GlyphData(a).Length > 0);
        Assert.True(loaded.GlyphData(b).Length > 0);
        Assert.Equal(0, loaded.GlyphData(font.GlyphFor('C')).Length);   // not requested: no outline kept
        Assert.True(loaded.GlyphData(0).Length > 0);                    // .notdef is always kept
    }

    [Fact]
    public void The_checksum_adjustment_makes_the_whole_file_sum_to_the_magic_number()
    {
        var subset = TrueTypeSubsetter.Subset(Sans(), [Sans().GlyphFor('x')]);
        uint sum = 0;
        for (int i = 0; i < subset.Length; i += 4)
        {
            uint word = 0;
            for (int k = 0; k < 4; k++) word = (word << 8) | (i + k < subset.Length ? subset[i + k] : (uint)0);
            sum += word;
        }
        Assert.Equal(0xB1B0AFBAu, sum);
    }

    [Fact]
    public void Composite_glyphs_keep_their_components()
    {
        // Accented letters in DejaVu are composites of a base letter and an accent.
        var font = Sans();
        int eacute = font.GlyphFor(0x00E9), e = font.GlyphFor('e');
        var data = font.GlyphData(eacute);
        Assert.True(data.Length > 10);
        var loaded = TrueTypeFont.Load(TrueTypeSubsetter.Subset(font, [eacute]));
        Assert.True(loaded.GlyphData(eacute).Length > 0);
        if ((short)((data[0] << 8) | data[1]) < 0)   // composite: its base glyph must have come along
            Assert.True(loaded.GlyphData(e).Length > 0);
    }

    [Fact]
    public void The_subset_tables_form_a_valid_font()
    {
        var subset = TrueTypeSubsetter.Subset(Sans(), Enumerable.Range('a', 26).Select(c => (int)Sans().GlyphFor((char)c)));
        var font = TrueTypeFont.Load(subset);
        Assert.True(font.HasGlyphOutlines);
        Assert.True(font.TryGetTable("head", out _) && font.TryGetTable("glyf", out _) && font.TryGetTable("loca", out _) && font.TryGetTable("hmtx", out _));
        Assert.Equal(1, font.HeadIndexToLocFormat);
    }
}

public class FontLibraryTests
{
    [Fact]
    public void Finds_fonts_in_the_configured_folders_only()
    {
        var families = TestFonts.Library.FamilyNames.ToList();
        Assert.Contains("AReport Test Sans", families);
        Assert.Contains("AReport Test CJK", families);
        Assert.Equal(3, TestFonts.Library.Entries.Count);
    }

    [Fact]
    public void Matches_families_ignoring_case_and_font_variant_suffixes()
    {
        Assert.NotNull(TestFonts.Library.Find("areport test sans", false, false));
        Assert.NotNull(TestFonts.Library.Find("AReport Test Sans(FaNum)", false, false));
        Assert.Null(TestFonts.Library.Find("No Such Font", false, false));
        Assert.Null(TestFonts.Library.Find("", false, false));
    }

    [Fact]
    public void Uses_the_real_bold_face_when_it_exists_and_synthesizes_when_it_does_not()
    {
        var bold = TestFonts.Library.Find("AReport Test Sans", bold: true, italic: false)!;
        Assert.True(bold.Font.Names.Bold);
        Assert.False(bold.SyntheticBold);

        var italic = TestFonts.Library.Find("AReport Test Sans", bold: false, italic: true)!;
        Assert.True(italic.SyntheticItalic);
        Assert.False(italic.SyntheticBold);

        var cjkBold = TestFonts.Library.Find("AReport Test CJK", bold: true, italic: false)!;
        Assert.True(cjkBold.SyntheticBold);
    }

    [Fact]
    public void Faces_are_shared_between_requests() =>
        Assert.Same(TestFonts.Library.Find("AReport Test Sans", false, false), TestFonts.Library.Find("AReport Test Sans", false, false));

    [Fact]
    public void Finds_any_font_that_covers_a_character()
    {
        Assert.Equal("AReport Test CJK", TestFonts.Library.FindAnyCovering('中', false, false)!.Font.Names.Family);
        Assert.Equal("AReport Test Sans", TestFonts.Library.FindAnyCovering(0x0628, false, false)!.Font.Names.Family);
        Assert.Null(TestFonts.Library.FindAnyCovering(0x1F600, false, false));
    }

    [Fact]
    public void An_empty_or_missing_folder_gives_an_empty_library()
    {
        var options = new FontOptions { UseSystemFonts = false };
        options.Directories.Add(Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N")));
        Assert.Empty(new FontLibrary(options).Entries);
    }

    [Fact]
    public void Loads_fonts_from_a_collection_file_on_disk()
    {
        var dir = TestKit.TempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "pack.ttc"), TestFonts.ToCollection(File.ReadAllBytes(Path.Combine(TestFonts.Directory, "AReportTestSans-Regular.ttf"))));
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "not a font");
            File.WriteAllBytes(Path.Combine(dir, "broken.ttf"), [1, 2, 3, 4]);
            var options = new FontOptions { UseSystemFonts = false };
            options.Directories.Add(dir);
            var library = new FontLibrary(options);

            var entry = Assert.Single(library.Entries);
            Assert.Equal("AReport Test Sans", entry.Names.Family);
            Assert.True(library.Find("AReport Test Sans", false, false)!.Covers(0x067E));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class FontSystemTests
{
    private readonly FontSystem _fonts = TestFonts.Fonts();

    private static readonly FontSpec Helvetica = new("Helvetica", 10, false, false);

    [Fact]
    public void Latin_text_in_a_standard_family_stays_on_the_built_in_fonts()
    {
        var face = _fonts.FaceFor(Helvetica, 'A', null);
        Assert.True(face!.IsStandard);
        Assert.Equal('A', face.CodeFor('A'));
    }

    [Fact]
    public void Persian_text_in_a_standard_family_falls_back_to_an_embedded_font()
    {
        var face = _fonts.FaceFor(Helvetica, 0x0628, null);
        Assert.False(face!.IsStandard);
        Assert.True(face.Covers(0x0628));
    }

    [Fact]
    public void Chinese_text_uses_the_cjk_font()
    {
        var face = _fonts.FaceFor(Helvetica, '中', null);
        Assert.Equal("AReport Test CJK", ((TrueTypeFace)face!).Font.Names.Family);
    }

    [Fact]
    public void A_requested_embedded_family_is_used_for_latin_too()
    {
        var face = _fonts.FaceFor(new FontSpec("AReport Test Sans", 10, false, false), 'A', null);
        Assert.False(face!.IsStandard);
    }

    [Fact]
    public void An_unknown_family_falls_back_with_a_warning_once_the_script_needs_it()
    {
        var warnings = new List<string>();
        var face = _fonts.FaceFor(new FontSpec("Imaginary Font", 10, false, false), 'A', null, warnings.Add);
        Assert.True(face!.IsStandard);   // Latin still prints, in Helvetica
        Assert.Contains("Imaginary Font", Assert.Single(warnings));
    }

    [Fact]
    public void Spaces_and_digits_stay_in_the_font_of_the_surrounding_text()
    {
        var sans = _fonts.FaceFor(new FontSpec("AReport Test Sans", 10, false, false), 0x0628, null)!;
        Assert.Same(sans, _fonts.FaceFor(Helvetica, ' ', sans));
        Assert.Same(sans, _fonts.FaceFor(Helvetica, '1', sans));
    }

    [Fact]
    public void Characters_nobody_has_return_null()
    {
        Assert.Null(_fonts.FaceFor(Helvetica, 0x1F600, null));
        Assert.Null(FontSystem.StandardOnly.FaceFor(Helvetica, 0x0628, null));
    }

    [Fact]
    public void Standard_faces_report_widths_from_the_afm_tables()
    {
        var face = FontSystem.StandardOnly.Standard(Helvetica);
        Assert.Equal(722, face.Advance1000(face.CodeFor('H')));
        Assert.Equal(-1, face.CodeFor(0x0628));
        Assert.Equal("std:Helvetica", face.Key);
    }

    [Fact]
    public void Standard_alias_detection()
    {
        Assert.True(StandardFonts.IsStandardAlias("Arial"));
        Assert.True(StandardFonts.IsStandardAlias(" times new roman "));
        Assert.False(StandardFonts.IsStandardAlias("Tahoma"));
        Assert.False(StandardFonts.IsStandardAlias(null));
    }
}
