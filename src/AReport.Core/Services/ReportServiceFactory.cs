using AReport.Data;
using AReport.Fonts;
using AReport.Layout;
using AReport.Pdf;
using AReport.Serialization;
using AReport.Templates;

namespace AReport.Services;

/// <summary>Builds the default object graph. Used by the DI extension, the designer and tests; use it directly without DI.</summary>
public static class ReportServiceFactory
{
    public static IReportService Create(ReportOptions? options = null, TimeProvider? time = null, IDesignSource? designSource = null, FontLibrary? fontLibrary = null)
    {
        options ??= new ReportOptions();
        time ??= TimeProvider.System;

        var resolver = new ValueResolver();
        var fonts = new FontSystem(fontLibrary ?? new FontLibrary(options.Fonts), options.Fonts);
        var layout = new LayoutEngine(new StandardFontMetrics(), new TemplateEngine(resolver), resolver, options, time, fonts);

        return new ReportService(
            designSource ?? new FileSystemDesignSource(options, new ArtSerializer()),
            new DesignValidator(),
            layout,
            new PdfRenderer(options, time),
            new PdfOutputValidator());
    }
}
