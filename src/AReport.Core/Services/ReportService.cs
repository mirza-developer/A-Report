using AReport.Data;
using AReport.Layout;
using AReport.Model;
using AReport.Pdf;

namespace AReport.Services;

/// <summary>design source → validation → layout → renderer → output check. Each step is an injectable abstraction.</summary>
public sealed class ReportService(
    IDesignSource source,
    IDesignValidator validator,
    ILayoutEngine layout,
    IReportRenderer renderer,
    IOutputValidator outputValidator) : IReportService
{
    public async Task<ReportResult> GenerateAsync(string reportName, ReportData data, string? title = null, CancellationToken cancellationToken = default)
    {
        var design = await source.LoadAsync(reportName, cancellationToken).ConfigureAwait(false);
        return await GenerateAsync(design, data, title, cancellationToken).ConfigureAwait(false);
    }

    public Task<ReportResult> GenerateAsync(ReportDesign design, ReportData data, string? title = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        // Layout and rendering are CPU-bound: keep them off the caller's (UI / circuit) thread.
        return Task.Run(() => Generate(design, data, title), cancellationToken);
    }

    private ReportResult Generate(ReportDesign design, ReportData data, string? title)
    {
        var issues = validator.Validate(design);
        if (issues.Count > 0)
            throw new ReportDesignException(issues);

        var document = layout.Layout(design, data, title);
        var content = renderer.Render(document);
        outputValidator.Validate(content);

        return new ReportResult(content, FileNameFor(document.Title, renderer.FileExtension), renderer.ContentType,
            document.Pages.Count, document.Warnings);
    }

    internal static string FileNameFor(string title, string extension)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return (cleaned.Length == 0 ? "report" : cleaned) + extension;
    }
}
