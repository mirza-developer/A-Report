# A-Report

Design reports in a Windows app, render them to PDF inside your .NET 9 / Blazor Server app.

| Piece | What it is |
|---|---|
| **`AReport.Core`** (NuGet) | Loads `.art` report files, binds your data, paginates tables and writes PDF. No third-party dependencies. |
| **`AReport.Blazor`** (NuGet) | `AddAReport()` for DI, a `<ReportViewer>` component and a browser download helper for Blazor Server. |
| **`AReport.Designer`** | Windows (WPF) designer that creates and edits `.art` files. Follows [DESIGN.md](DESIGN.md). |

```
designer (.art file) ──► AReport.Core ──► PDF bytes ──► <ReportViewer> / download in the browser
                              ▲
                  your data (variables, tables, images)
```

## Quick start (Blazor Server)

```csharp
// Program.cs
builder.Services.AddAReport(o => o.ReportsDirectory = Path.Combine(builder.Environment.WebRootPath, "reports"));
```

Put `invoice.art` (made in the designer) in `wwwroot/reports/`, then:

```razor
@inject IReportService Reports
@inject IReportBrowser Browser

<ReportViewer Report="_report" Height="80vh" />
<button @onclick="Create">Create</button>

@code {
    private ReportResult? _report;

    private async Task Create()
    {
        var data = new ReportData()
            .AddVariable("InvoiceNumber", "INV-42")
            .AddVariable("Customer", new { Name = "Ada", Address = "12 Analytical Engine Road" })
            .AddDataSource("Lines", lines)                       // any IEnumerable, DataTable, list of dictionaries…
            .AddImageFile("logo", "wwwroot/img/logo.png");       // PNG or JPEG
        _report = await Reports.GenerateAsync("invoice", data, title: "Invoice 42");

        // or: await Browser.DownloadAsync(_report);
        // _report.Warnings lists unresolved placeholders, missing images, …
    }
}
```

Without Blazor or DI:

```csharp
var reports = ReportServiceFactory.Create(new ReportOptions { ReportsDirectory = "reports" });
ReportResult pdf = await reports.GenerateAsync("invoice", data);
File.WriteAllBytes(pdf.FileName, pdf.Content);
```

## Replacing the old `PdfExporter`

`ReportExporter` has the same shape as the old `PdfExporter` (lists of key/value pairs, images as file paths, a `MemoryStream` back):

```csharp
// before                                          // after
PdfExporter.WebRootPath = env.WebRootPath;         services.AddAReport(o => o.ReportsDirectory = Path.Combine(env.WebRootPath, "reports"));
var pdf = new PdfExporter();                       // inject ReportExporter instead
using var s = await pdf.DownloadPdf(               using var s = await exporter.DownloadPdf(
    "invoice", "Invoice", dataSources, images, variables);   "invoice", "Invoice", dataSources, images, variables);
```

Same behaviour as before: a missing image file is skipped silently, an empty or invalid result is an error, `PrintReport` returns the same kind of stream.
What changes: report files are `.art` (made with the designer) instead of Stimulsoft `.mrt`, and **no license key is needed**.

## The `.art` format

UTF-8 JSON, every length in millimetres, versioned (`"format": "art"`, `"schemaVersion": 1`). See [`samples/reports/invoice.art`](samples/reports/invoice.art).

| Element | Notes |
|---|---|
| `text` | Template with `{placeholders}`, font family (Helvetica / Times / Courier), size, bold, italic, colour, alignment, wrapping |
| `rectangle`, `line` | Fill / stroke colour (empty = none), stroke width |
| `image` | `imageKey` (supplied at run time) or `embeddedImageBase64`; fit `Contain` or `Stretch` |
| `table` | Repeats a row per item of a data source, header repeats on every page, column weights, formats, zebra rows |

Any element can set `repeatOnEveryPage` (headers, footers, watermarks); otherwise it appears on the first page.

**Placeholders:** `{Name}`, `{Customer.Address.City}`, `{Total:N2}`, `{Now:yyyy-MM-dd}`, `{PageNumber}`, `{PageCount}`; `{{` and `}}` are literal braces. The first segment is looked up as a built-in, then a variable, then the first row of a data source. Unresolved placeholders render empty and add a warning (set `StrictBindings` to make them throw).

**Layout rules worth knowing**
* Tables grow with the data and continue on following pages from the top margin. They stop at the **bottom margin**, so put footers *inside the margin* (see the sample: bottom margin 24 mm, footer at 277 mm).
* Use one growing table per report; content below it should be a repeating footer.
* Text that does not fit its box is clipped and reported as a warning.

## Limitations (v1)

* **Text is Latin only** (Windows-1252: English, French, German, Spanish… plus € – — ™). The 14 standard PDF fonts are used, so there is nothing to embed. Other scripts (Persian, Arabic, Chinese…) are drawn as `?` and reported as a warning. Unicode text needs embedded TrueType fonts plus shaping and bidi; the engine is built so a font-metrics/renderer implementation can be added (`IFontMetrics`, `IReportRenderer`).
* Images: PNG (8-bit, non-interlaced) and JPEG (grayscale/RGB). GIF/BMP/CMYK/16-bit are rejected with a warning.
* Times bold/italic widths are approximated by the regular widths.

## Building and testing

```
dotnet build A-Report.slnx
dotnet test  A-Report.slnx          # the designer needs Windows to build
```

* `tests/AReport.Core.Tests` – model, serializer, binding, layout, PDF structure (every xref offset is verified), services, the sample report and the design-system rules.
* `tests/AReport.Designer.Tests` – the designer's view-model (undo/redo, drag, property editing, files, preview) without any UI.
* `tests/AReport.Blazor.Tests` – DI registration.

## Publishing to NuGet

`AReport.Core` and `AReport.Blazor` are packable (SourceLink, symbols `.snupkg`, XML docs, README and icon, package validation).
Tag a release and CI does the rest:

```
git tag v1.0.0 && git push origin v1.0.0
```

The workflow needs a `NUGET_API_KEY` repository secret. Prerelease tags such as `v1.1.0-beta.1` publish prerelease packages.

## Design

SOLID/DRY in practice: one interface per pipeline step (`IDesignSource`, `IDesignSerializer`, `IDesignValidator`, `ILayoutEngine`, `IReportRenderer`, `IOutputValidator`, `IFontMetrics`, `ITemplateEngine`, `IValueResolver`), assembled by `ReportServiceFactory`; text wrapping, number formatting, colour parsing and PDF syntax each live in exactly one place.

## License

MIT, see [LICENSE](LICENSE).
