using AReport.Data;
using AReport.Fonts;
using AReport.Layout;
using AReport.Model;
using AReport.Pdf;
using AReport.Services;
using AReport.Templates;

namespace AReport.Core.Tests.Support;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Creates the pieces under test with deterministic settings.</summary>
internal static class TestKit
{
    public static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 30, 0, TimeSpan.Zero);

    public static readonly TimeProvider Time = new FixedTimeProvider(Now);

    public static ReportOptions Options(bool compress = false, bool strict = false, string? directory = null) => new()
    {
        CompressPdf = compress,
        StrictBindings = strict,
        ReportsDirectory = directory ?? "reports",
    };

    public static LayoutEngine Layout(ReportOptions? options = null)
    {
        var resolver = new ValueResolver();
        return new LayoutEngine(new StandardFontMetrics(), new TemplateEngine(resolver), resolver, options ?? Options(), Time);
    }

    public static IReportService Service(ReportOptions? options = null) => ReportServiceFactory.Create(options ?? Options(), Time);

    public static TemplateEngine Templates() => new(new ValueResolver());

    public static BindingContext Context(ReportData? data = null, int page = 1, int pages = 1, bool strict = false) =>
        new(data ?? new ReportData(), page, pages, Now, System.Globalization.CultureInfo.InvariantCulture, strict);

    public static ReportDesign Design(params ReportElement[] elements) => new() { Name = "Test", Elements = [.. elements] };

    public static TextElement Text(string text, double x = 10, double y = 10, double w = 100, double h = 10) =>
        new() { Text = text, X = x, Y = y, Width = w, Height = h };

    public static TableElement Table(string source, double y = 10, params (string Header, string Binding)[] columns) => new()
    {
        DataSource = source,
        Y = y,
        X = 10,
        Width = 190,
        Columns = columns.Select(c => new TableColumn { Header = c.Header, Binding = c.Binding }).ToList(),
    };

    public static List<object?> Rows(int count) =>
        Enumerable.Range(1, count).Select(i => (object?)new Dictionary<string, object?> { ["Name"] = $"Item {i}", ["Qty"] = i }).ToList();

    public static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "areport-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
