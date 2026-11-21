using AReport.Data;
using AReport.Model;

namespace AReport.Services;

/// <summary>The one entry point the application calls.</summary>
public interface IReportService
{
    /// <summary>Loads <paramref name="reportName"/> from the design source and renders it.</summary>
    Task<ReportResult> GenerateAsync(string reportName, ReportData data, string? title = null, CancellationToken cancellationToken = default);

    /// <summary>Renders a design you already have (for example one just edited in a designer).</summary>
    Task<ReportResult> GenerateAsync(ReportDesign design, ReportData data, string? title = null, CancellationToken cancellationToken = default);
}
