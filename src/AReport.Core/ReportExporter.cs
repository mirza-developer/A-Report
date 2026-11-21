using AReport.Data;
using AReport.Services;

namespace AReport;

/// <summary>
/// Drop-in replacement for the old <c>PdfExporter</c>: same method names and parameter shapes
/// (lists of key/value pairs, images as file paths), now backed by <see cref="IReportService"/>.
/// </summary>
public sealed class ReportExporter(IReportService reports)
{
    public async Task<MemoryStream> DownloadPdf(
        string reportFile,
        string title,
        IEnumerable<KeyValuePair<string, object>> dataSources,
        IEnumerable<KeyValuePair<string, string>> images,
        IEnumerable<KeyValuePair<string, object>> variables,
        CancellationToken cancellationToken = default)
    {
        var data = ReportData.FromLegacy(dataSources, images, variables);
        var result = await reports
            .GenerateAsync(reportFile, data, string.IsNullOrWhiteSpace(title) ? null : title, cancellationToken)
            .ConfigureAwait(false);
        return result.ToStream();
    }

    public Task<MemoryStream> PrintReport(
        string reportFile,
        IEnumerable<KeyValuePair<string, object>> dataSources,
        IEnumerable<KeyValuePair<string, string>> images,
        IEnumerable<KeyValuePair<string, object>> variables,
        CancellationToken cancellationToken = default)
        => DownloadPdf(reportFile, "", dataSources, images, variables, cancellationToken);
}
