using AReport.Core.Tests.Support;
using AReport.Data;
using AReport.Layout;
using AReport.Model;
using AReport.Pdf;
using AReport.Serialization;
using AReport.Services;

namespace AReport.Core.Tests;

public class DesignValidatorTests
{
    private readonly DesignValidator _validator = new();

    [Fact]
    public void A_default_design_is_valid() => Assert.Empty(_validator.Validate(TestKit.Design(TestKit.Text("x"))));

    [Fact]
    public void Flags_bad_page_settings()
    {
        var design = TestKit.Design();
        design.Page.WidthMm = 0;
        design.Page.MarginLeftMm = -1;
        var issues = _validator.Validate(design);
        Assert.Contains(issues, i => i.Contains("page size"));
        Assert.Contains(issues, i => i.Contains("negative"));
    }

    [Fact]
    public void Flags_margins_that_leave_no_printable_area()
    {
        var design = TestKit.Design();
        design.Page.MarginTopMm = 200;
        design.Page.MarginBottomMm = 100;
        Assert.Contains(_validator.Validate(design), i => i.Contains("no printable area"));
    }

    [Fact]
    public void Flags_duplicate_ids_and_negative_sizes()
    {
        var a = TestKit.Text("a");
        var b = TestKit.Text("b");
        b.Id = a.Id;
        b.Width = -1;
        var issues = _validator.Validate(TestKit.Design(a, b));
        Assert.Contains(issues, i => i.Contains("used more than once"));
        Assert.Contains(issues, i => i.Contains("negative size"));
    }

    [Fact]
    public void Flags_bad_text_and_image_elements()
    {
        var text = TestKit.Text("a");
        text.FontSize = 0;
        var issues = _validator.Validate(TestKit.Design(text, new ImageElement()));
        Assert.Contains(issues, i => i.Contains("font size"));
        Assert.Contains(issues, i => i.Contains("image key or embedded"));
    }

    [Fact]
    public void Flags_incomplete_tables()
    {
        var table = new TableElement { RowHeightMm = 0, Width = 0 };
        table.Columns.Add(new TableColumn { Weight = 0 });
        var issues = _validator.Validate(TestKit.Design(table));
        Assert.Contains(issues, i => i.Contains("data source"));
        Assert.Contains(issues, i => i.Contains("weight"));
        Assert.Contains(issues, i => i.Contains("heights"));
        Assert.Contains(issues, i => i.Contains("width"));
        Assert.Contains(_validator.Validate(TestKit.Design(new TableElement { DataSource = "x" })), i => i.Contains("at least one column"));
    }
}

public class FileSystemDesignSourceTests : IDisposable
{
    private readonly string _dir = TestKit.TempDirectory();
    private readonly FileSystemDesignSource _source;

    public FileSystemDesignSourceTests() =>
        _source = new FileSystemDesignSource(TestKit.Options(directory: _dir), new ArtSerializer());

    public void Dispose() => Directory.Delete(_dir, true);

    private async Task Save(string name, string? title = null)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await new ArtSerializer().SaveToFileAsync(new ReportDesign { Name = title ?? name }, path);
    }

    [Fact]
    public async Task Loads_by_name_with_or_without_the_extension()
    {
        await Save("invoice.art", "Invoice");
        Assert.Equal("Invoice", (await _source.LoadAsync("invoice")).Name);
        Assert.Equal("Invoice", (await _source.LoadAsync("invoice.art")).Name);
        Assert.Equal("Invoice", (await _source.LoadAsync("INVOICE.ART".ToLowerInvariant())).Name);
    }

    [Fact]
    public async Task Loads_from_sub_folders()
    {
        await Save("sales/q3.art", "Q3");
        Assert.Equal("Q3", (await _source.LoadAsync("sales/q3")).Name);
    }

    [Fact]
    public async Task Missing_reports_raise_ReportNotFoundException()
    {
        var ex = await Assert.ThrowsAsync<ReportNotFoundException>(() => _source.LoadAsync("nope"));
        Assert.Equal("nope", ex.ReportName);
        Assert.DoesNotContain(_dir, ex.Message);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("a/../../secret")]
    [InlineData("/etc/passwd")]
    public async Task Names_cannot_escape_the_reports_directory(string name) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _source.LoadAsync(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_names_are_rejected(string name) =>
        await Assert.ThrowsAsync<ArgumentException>(() => _source.LoadAsync(name));

    [Fact]
    public async Task A_corrupt_file_raises_ArtFormatException()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "bad.art"), "not json");
        await Assert.ThrowsAsync<ArtFormatException>(() => _source.LoadAsync("bad"));
    }
}

public class ReportServiceTests : IDisposable
{
    private readonly string _dir = TestKit.TempDirectory();

    public void Dispose() => Directory.Delete(_dir, true);

    private IReportService Service() => TestKit.Service(TestKit.Options(directory: _dir));

    private async Task Save(string name, ReportDesign design) =>
        await new ArtSerializer().SaveToFileAsync(design, Path.Combine(_dir, name + ".art"));

    [Fact]
    public async Task Generates_a_pdf_from_an_art_file_end_to_end()
    {
        await Save("hello", TestKit.Design(TestKit.Text("Hello {Name}")));
        var result = await Service().GenerateAsync("hello", new ReportData().AddVariable("Name", "Ada"));

        var pdf = PdfInspector.Parse(result.Content);
        pdf.VerifyStructure();
        Assert.True(pdf.HasText("Hello Ada"));
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(1, result.PageCount);
        Assert.Equal("Test.pdf", result.FileName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task A_title_names_the_file_and_the_document()
    {
        await Save("t", TestKit.Design());
        var result = await Service().GenerateAsync("t", new ReportData(), "Q3 / 2026");

        Assert.Equal("Q3 _ 2026.pdf", result.FileName);
        Assert.Contains("/Title (Q3 / 2026)", PdfInspector.Parse(result.Content).Text);
    }

    [Fact]
    public async Task Warnings_reach_the_caller()
    {
        await Save("w", TestKit.Design(TestKit.Text("{Nope}")));
        var result = await Service().GenerateAsync("w", new ReportData());
        Assert.Contains("Nope", Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task Generates_from_a_design_object_without_touching_the_disk()
    {
        var result = await Service().GenerateAsync(TestKit.Design(TestKit.Text("Direct")), new ReportData());
        Assert.True(PdfInspector.Parse(result.Content).HasText("Direct"));
    }

    [Fact]
    public async Task Invalid_designs_throw_with_every_issue()
    {
        var design = TestKit.Design(new TableElement(), new ImageElement());
        var ex = await Assert.ThrowsAsync<ReportDesignException>(() => Service().GenerateAsync(design, new ReportData()));
        Assert.True(ex.Issues.Count >= 3);
        Assert.Contains("data source", ex.Message);
    }

    [Fact]
    public async Task A_missing_report_is_reported()
    {
        await Assert.ThrowsAsync<ReportNotFoundException>(() => Service().GenerateAsync("missing", new ReportData()));
    }

    [Fact]
    public async Task A_cancelled_token_stops_generation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().GenerateAsync(TestKit.Design(), new ReportData(), null, cts.Token));
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().GenerateAsync((ReportDesign)null!, new ReportData()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Service().GenerateAsync(TestKit.Design(), null!));
    }

    [Fact]
    public async Task Bad_renderer_output_is_caught_by_the_output_validator()
    {
        var service = new ReportService(
            new FileSystemDesignSource(TestKit.Options(directory: _dir), new ArtSerializer()),
            new DesignValidator(),
            TestKit.Layout(),
            new EmptyRenderer(),
            new PdfOutputValidator());

        await Assert.ThrowsAsync<ReportRenderException>(() => service.GenerateAsync(TestKit.Design(), new ReportData()));
    }

    [Fact]
    public async Task Every_step_can_be_replaced()
    {
        var source = new InMemorySource(TestKit.Design(TestKit.Text("From memory")));
        var service = TestKit.Service(TestKit.Options());
        var viaFactory = ReportServiceFactory.Create(TestKit.Options(), TestKit.Time, source);

        var result = await viaFactory.GenerateAsync("anything", new ReportData());
        Assert.True(PdfInspector.Parse(result.Content).HasText("From memory"));
        Assert.NotNull(service);
    }

    [Theory]
    [InlineData("Invoice", "Invoice.pdf")]
    [InlineData("  ", "report.pdf")]
    [InlineData("a/b", "a_b.pdf")]
    public void File_names_are_sanitized(string title, string expected) =>
        Assert.Equal(expected, ReportService.FileNameFor(title, ".pdf"));

    [Fact]
    public void Results_expose_a_fresh_read_only_stream_each_time()
    {
        var result = new ReportResult([1, 2, 3], "x.pdf", "application/pdf", 1, []);
        using var first = result.ToStream();
        Assert.Equal(0, first.Position);
        Assert.False(first.CanWrite);
        Assert.Equal(new byte[] { 1, 2, 3 }, first.ToArray());
        using var second = result.ToStream();
        Assert.Equal(0, second.Position);
    }

    private sealed class EmptyRenderer : IReportRenderer
    {
        public string ContentType => "application/pdf";
        public string FileExtension => ".pdf";
        public byte[] Render(LaidOutDocument document) => [];
    }

    private sealed class InMemorySource(ReportDesign design) : IDesignSource
    {
        public Task<ReportDesign> LoadAsync(string reportName, CancellationToken cancellationToken = default) => Task.FromResult(design);
    }
}

public class ReportExporterTests : IDisposable
{
    private readonly string _dir = TestKit.TempDirectory();

    public void Dispose() => Directory.Delete(_dir, true);

    private ReportExporter Exporter() => new(TestKit.Service(TestKit.Options(directory: _dir)));

    private async Task Save(string name, ReportDesign design) =>
        await new ArtSerializer().SaveToFileAsync(design, Path.Combine(_dir, name + ".art"));

    private static readonly List<KeyValuePair<string, object>> NoObjects = [];
    private static readonly List<KeyValuePair<string, string>> NoImages = [];

    [Fact]
    public async Task DownloadPdf_works_with_the_old_call_shape()
    {
        await Save("invoice", TestKit.Design(
            TestKit.Text("Customer: {Customer}"),
            TestKit.Table("Lines", 30, ("Item", "Name"), ("Qty", "Qty"))));

        var dataSources = new List<KeyValuePair<string, object>> { new("Lines", TestKit.Rows(3)) };
        var variables = new List<KeyValuePair<string, object>> { new("Customer", "Ada") };

        using var stream = await Exporter().DownloadPdf("invoice", "Invoice 7", dataSources, NoImages, variables);

        Assert.Equal(0, stream.Position);
        var pdf = PdfInspector.Parse(stream.ToArray());
        pdf.VerifyStructure();
        Assert.True(pdf.HasText("Customer: Ada"));
        Assert.True(pdf.HasText("Item 2"));
        Assert.Contains("/Title (Invoice 7)", pdf.Text);
    }

    [Fact]
    public async Task PrintReport_returns_the_same_kind_of_stream()
    {
        await Save("p", TestKit.Design(TestKit.Text("Print me")));
        using var stream = await Exporter().PrintReport("p", NoObjects, NoImages, NoObjects);
        Assert.True(PdfInspector.Parse(stream.ToArray()).HasText("Print me"));
    }

    [Fact]
    public async Task A_blank_title_falls_back_to_the_design_name()
    {
        await Save("p", TestKit.Design());
        using var stream = await Exporter().DownloadPdf("p", "", NoObjects, NoImages, NoObjects);
        Assert.Contains("/Title (Test)", PdfInspector.Parse(stream.ToArray()).Text);
    }

    [Fact]
    public async Task Images_are_passed_as_file_paths_and_missing_files_are_skipped()
    {
        await Save("logo", TestKit.Design(new ImageElement { ImageKey = "logo", Width = 20, Height = 20 }));
        var file = Path.Combine(_dir, "logo.png");
        await File.WriteAllBytesAsync(file, TestImages.Png(4, 4, 2, (_, _) => [1, 2, 3]));

        using var withLogo = await Exporter().DownloadPdf("logo", "t", NoObjects,
            [new KeyValuePair<string, string>("logo", file)], NoObjects);
        Assert.Contains("/Subtype /Image", PdfInspector.Parse(withLogo.ToArray()).Text);

        using var withoutLogo = await Exporter().DownloadPdf("logo", "t", NoObjects,
            [new KeyValuePair<string, string>("logo", Path.Combine(_dir, "gone.png"))], NoObjects);
        Assert.DoesNotContain("/Subtype /Image", PdfInspector.Parse(withoutLogo.ToArray()).Text);
    }

    [Fact]
    public async Task Missing_reports_surface_as_ReportNotFoundException() =>
        await Assert.ThrowsAsync<ReportNotFoundException>(() => Exporter().DownloadPdf("nope", "t", NoObjects, NoImages, NoObjects));
}

public class SampleReportTests
{
    private static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);

    [Fact]
    public async Task The_invoice_sample_loads_and_renders_with_its_sample_data()
    {
        var design = await new ArtSerializer().LoadFromFileAsync(SamplePath("invoice.art"));
        var data = ReportData.FromJson(await File.ReadAllTextAsync(SamplePath("invoice.data.json")));

        var result = await TestKit.Service().GenerateAsync(design, data, "Invoice");

        var pdf = PdfInspector.Parse(result.Content);
        pdf.VerifyStructure();
        Assert.Empty(result.Warnings);
        Assert.True(result.PageCount >= 2, "the sample has enough lines to span several pages");
        Assert.True(pdf.HasText("Invoice INV-2026-0042"));
        Assert.True(pdf.HasText("Page 1 of " + result.PageCount));
    }

    [Fact]
    public async Task The_invoice_sample_renders_with_generated_placeholder_data_too()
    {
        var design = await new ArtSerializer().LoadFromFileAsync(SamplePath("invoice.art"));
        var result = await TestKit.Service().GenerateAsync(design, SampleData.CreateFor(design));
        Assert.Empty(result.Warnings.Where(w => !w.Contains("Image")));
    }
}
