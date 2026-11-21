using System.Text;
using AReport.Core.Tests.Support;
using AReport.Model;
using AReport.Serialization;

namespace AReport.Core.Tests;

public class RgbColorTests
{
    [Fact]
    public void Parses_six_digit_hex() => Assert.Equal(new RgbColor(255, 128, 0), Parse("#FF8000"));

    [Fact]
    public void Parses_three_digit_hex() => Assert.Equal(new RgbColor(255, 136, 0), Parse("#f80"));

    [Theory]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#GG0000")]
    [InlineData("FF0000")]
    public void Rejects_invalid_colors(string text) => Assert.False(RgbColor.TryParse(text, out _));

    [Fact]
    public void Rejects_null() => Assert.False(RgbColor.TryParse(null, out _));

    [Fact]
    public void ToString_round_trips() => Assert.Equal("#0A0B0C", new RgbColor(10, 11, 12).ToString());

    private static RgbColor Parse(string text)
    {
        Assert.True(RgbColor.TryParse(text, out var color));
        return color;
    }
}

public class UnitsTests
{
    [Fact]
    public void One_inch_is_72_points() => Assert.Equal(72.0, Units.MmToPt(25.4), 9);

    [Fact]
    public void Conversion_round_trips() => Assert.Equal(12.5, Units.PtToMm(Units.MmToPt(12.5)), 9);
}

public class ArtSerializerTests
{
    private readonly ArtSerializer _serializer = new();

    private static ReportDesign SampleDesign() => new()
    {
        Name = "Sample",
        Page = PageSettings.A4Landscape(),
        Elements =
        [
            new TextElement { Name = "title", Text = "Hello {Name}", Bold = true, HorizontalAlignment = HAlign.Center, VerticalAlignment = VAlign.Middle, RepeatOnEveryPage = true },
            new RectangleElement { FillColor = "#EEEEEE", StrokeColor = null },
            new LineElement { Width = 80, Height = 5 },
            new ImageElement { ImageKey = "logo", Fit = ImageFit.Stretch },
            new TableElement
            {
                DataSource = "Items",
                ZebraColor = "#F3F4F6",
                Columns = [new TableColumn { Header = "Qty", Binding = "Qty", Weight = 2, Alignment = HAlign.Right, Format = "N2" }],
            },
        ],
    };

    [Fact]
    public void Round_trip_preserves_every_element_type_and_value()
    {
        var json = _serializer.Serialize(SampleDesign());
        var back = _serializer.Deserialize(json);

        Assert.Equal("Sample", back.Name);
        Assert.Equal(297, back.Page.WidthMm);
        Assert.Equal(5, back.Elements.Count);

        var text = Assert.IsType<TextElement>(back.Elements[0]);
        Assert.Equal("Hello {Name}", text.Text);
        Assert.True(text.Bold);
        Assert.True(text.RepeatOnEveryPage);
        Assert.Equal(HAlign.Center, text.HorizontalAlignment);
        Assert.Equal(VAlign.Middle, text.VerticalAlignment);

        var rect = Assert.IsType<RectangleElement>(back.Elements[1]);
        Assert.Equal("#EEEEEE", rect.FillColor);
        Assert.Null(rect.StrokeColor);

        var line = Assert.IsType<LineElement>(back.Elements[2]);
        Assert.Equal(5, line.Height);

        var image = Assert.IsType<ImageElement>(back.Elements[3]);
        Assert.Equal("logo", image.ImageKey);
        Assert.Equal(ImageFit.Stretch, image.Fit);

        var table = Assert.IsType<TableElement>(back.Elements[4]);
        Assert.Equal("Items", table.DataSource);
        var column = Assert.Single(table.Columns);
        Assert.Equal(HAlign.Right, column.Alignment);
        Assert.Equal("N2", column.Format);
        Assert.Equal(2, column.Weight);
    }

    [Fact]
    public void Json_uses_readable_names_and_a_type_discriminator()
    {
        var json = _serializer.Serialize(SampleDesign());

        Assert.Contains("\"format\": \"art\"", json);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"type\": \"text\"", json);
        Assert.Contains("\"type\": \"table\"", json);
        Assert.Contains("\"horizontalAlignment\": \"Center\"", json);
        Assert.DoesNotContain("\"kind\"", json);
    }

    [Fact]
    public void Rejects_a_document_with_another_format()
    {
        var ex = Assert.Throws<ArtFormatException>(() => _serializer.Deserialize("{\"format\":\"pdf\",\"schemaVersion\":1}"));
        Assert.Contains("Not an .art document", ex.Message);
    }

    [Fact]
    public void Rejects_a_newer_schema_version()
    {
        var ex = Assert.Throws<ArtFormatException>(() => _serializer.Deserialize("{\"format\":\"art\",\"schemaVersion\":99}"));
        Assert.Contains("schema version 99", ex.Message);
    }

    [Fact]
    public void Rejects_a_zero_schema_version() =>
        Assert.Throws<ArtFormatException>(() => _serializer.Deserialize("{\"format\":\"art\",\"schemaVersion\":0}"));

    [Fact]
    public void Rejects_invalid_json() =>
        Assert.Throws<ArtFormatException>(() => _serializer.Deserialize("{ not json"));

    [Fact]
    public void Rejects_null_json() =>
        Assert.Throws<ArtFormatException>(() => _serializer.Deserialize("null"));

    [Fact]
    public void Rejects_an_unknown_element_type()
    {
        const string json = "{\"format\":\"art\",\"schemaVersion\":1,\"elements\":[{\"type\":\"hologram\"}]}";
        Assert.Throws<ArtFormatException>(() => _serializer.Deserialize(json));
    }

    [Fact]
    public void Accepts_the_type_discriminator_anywhere_in_the_object()
    {
        const string json = "{\"format\":\"art\",\"schemaVersion\":1,\"elements\":[{\"text\":\"Hi\",\"type\":\"text\"}]}";
        var design = _serializer.Deserialize(json);
        Assert.Equal("Hi", Assert.IsType<TextElement>(Assert.Single(design.Elements)).Text);
    }

    [Fact]
    public void Tolerates_comments_trailing_commas_and_a_byte_order_mark()
    {
        const string json = "\uFEFF{ // hand edited\n \"format\":\"ART\", \"schemaVersion\":1, \"name\":\"X\", }";
        Assert.Equal("X", _serializer.Deserialize(json).Name);
    }

    [Fact]
    public void Missing_page_and_elements_get_defaults()
    {
        var design = _serializer.Deserialize("{\"format\":\"art\",\"schemaVersion\":1,\"page\":null,\"elements\":null}");
        Assert.NotNull(design.Page);
        Assert.Empty(design.Elements);
    }

    [Fact]
    public async Task Stream_save_and_load_round_trip()
    {
        using var stream = new MemoryStream();
        await _serializer.SaveAsync(SampleDesign(), stream);
        stream.Position = 0;
        var back = await _serializer.LoadAsync(stream);
        Assert.Equal(5, back.Elements.Count);
    }

    [Fact]
    public async Task File_save_and_load_round_trip_and_files_are_utf8_without_bom()
    {
        var dir = TestKit.TempDirectory();
        try
        {
            var path = Path.Combine(dir, "x.art");
            var design = SampleDesign();
            design.Name = "Größe – 报告";
            await _serializer.SaveToFileAsync(design, path);

            Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
            var back = await _serializer.LoadFromFileAsync(path);
            Assert.Equal("Größe – 报告", back.Name);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Loading_a_stream_with_bad_json_throws_ArtFormatException()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("[1,2,3]"));
        await Assert.ThrowsAsync<ArtFormatException>(() => _serializer.LoadAsync(stream));
    }

    [Fact]
    public void Serialize_stamps_format_and_version()
    {
        var design = new ReportDesign { Format = "other", SchemaVersion = 7 };
        var json = _serializer.Serialize(design);
        Assert.Contains("\"format\": \"art\"", json);
        Assert.Equal(ArtFormat.CurrentVersion, design.SchemaVersion);
    }

    [Fact]
    public void Element_labels_prefer_the_name()
    {
        Assert.Equal("title", new TextElement { Name = "title" }.Label);
        Assert.StartsWith("Text ", new TextElement { Id = "abcdef123" }.Label);
    }
}
