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

/// <summary>The small fonts shipped with the tests (see Fonts/LICENSE-fonts.txt); system fonts are never used.</summary>
internal static class TestFonts
{
    public static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "Fonts");

    public static FontOptions Options()
    {
        var options = new FontOptions { UseSystemFonts = false };
        options.Directories.Add(Directory);
        return options;
    }

    /// <summary>One shared library, so the font files are scanned and parsed once for the whole test run.</summary>
    public static readonly FontLibrary Library = new(Options());

    public static FontSystem Fonts(FontOptions? options = null) => new(Library, options ?? Options());

    /// <summary>Wraps a .ttf into a one-face TrueType collection (.ttc).</summary>
    public static byte[] ToCollection(byte[] ttf)
    {
        const int headerSize = 16;
        var ttc = new byte[headerSize + ttf.Length];
        Array.Copy(ttf, 0, ttc, headerSize, ttf.Length);
        System.Text.Encoding.ASCII.GetBytes("ttcf").CopyTo(ttc, 0);
        ttc[4] = 0; ttc[5] = 1; ttc[6] = 0; ttc[7] = 0;                  // version 1.0
        ttc[8] = 0; ttc[9] = 0; ttc[10] = 0; ttc[11] = 1;                // one face
        ttc[12] = 0; ttc[13] = 0; ttc[14] = 0; ttc[15] = headerSize;     // its table directory

        int tables = (ttf[4] << 8) | ttf[5];
        for (int i = 0; i < tables; i++)
        {
            int offsetField = headerSize + 12 + 16 * i + 8;               // table offsets are relative to the file start
            uint offset = (uint)((ttc[offsetField] << 24) | (ttc[offsetField + 1] << 16) | (ttc[offsetField + 2] << 8) | ttc[offsetField + 3]) + headerSize;
            ttc[offsetField] = (byte)(offset >> 24); ttc[offsetField + 1] = (byte)(offset >> 16); ttc[offsetField + 2] = (byte)(offset >> 8); ttc[offsetField + 3] = (byte)offset;
        }
        return ttc;
    }
}

/// <summary>Creates the pieces under test with deterministic settings.</summary>
internal static class TestKit
{
    public static readonly DateTimeOffset Now = new(2026, 11, 20, 8, 30, 0, TimeSpan.Zero);

    public static readonly TimeProvider Time = new FixedTimeProvider(Now);

    public static ReportOptions Options(bool compress = false, bool strict = false, string? directory = null)
    {
        var options = new ReportOptions
        {
            CompressPdf = compress,
            StrictBindings = strict,
            ReportsDirectory = directory ?? "reports",
        };
        options.Fonts.UseSystemFonts = false;
        options.Fonts.Directories.Add(TestFonts.Directory);
        return options;
    }

    public static LayoutEngine Layout(ReportOptions? options = null)
    {
        var resolver = new ValueResolver();
        options ??= Options();
        return new LayoutEngine(new StandardFontMetrics(), new TemplateEngine(resolver), resolver, options, Time, TestFonts.Fonts(options.Fonts));
    }

    /// <summary>A layout engine that only knows the built-in PDF fonts (Latin text).</summary>
    public static LayoutEngine LayoutStandardFontsOnly()
    {
        var resolver = new ValueResolver();
        return new LayoutEngine(new StandardFontMetrics(), new TemplateEngine(resolver), resolver, Options(), Time, FontSystem.StandardOnly);
    }

    public static IReportService Service(ReportOptions? options = null) => ReportServiceFactory.Create(options ?? Options(), Time, null, TestFonts.Library);

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
