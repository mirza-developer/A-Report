using AReport.Services;
using Microsoft.JSInterop;

namespace AReport.Blazor;

/// <summary>Sends generated reports to the user's browser (Blazor Server: the bytes travel over the circuit as a stream).</summary>
public interface IReportBrowser
{
    /// <summary>Saves the report on the user's computer.</summary>
    ValueTask DownloadAsync(ReportResult report);

    ValueTask DownloadAsync(byte[] content, string fileName, string contentType = "application/pdf");

    /// <summary>Creates a blob: URL the browser can display (for example in an iframe). Call <see cref="RevokeUrlAsync"/> when done.</summary>
    ValueTask<string> CreateUrlAsync(ReportResult report);

    ValueTask RevokeUrlAsync(string url);
}

public sealed class ReportBrowser(IJSRuntime js) : IReportBrowser, IAsyncDisposable
{
    private IJSObjectReference? _module;

    public ValueTask DownloadAsync(ReportResult report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return DownloadAsync(report.Content, report.FileName, report.ContentType);
    }

    public async ValueTask DownloadAsync(byte[] content, string fileName, string contentType = "application/pdf")
    {
        ArgumentNullException.ThrowIfNull(content);
        var module = await ModuleAsync();
        using var stream = new MemoryStream(content, writable: false);
        using var reference = new DotNetStreamReference(stream);
        await module.InvokeVoidAsync("download", fileName, contentType, reference);
    }

    public async ValueTask<string> CreateUrlAsync(ReportResult report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var module = await ModuleAsync();
        using var stream = new MemoryStream(report.Content, writable: false);
        using var reference = new DotNetStreamReference(stream);
        return await module.InvokeAsync<string>("createObjectUrl", reference, report.ContentType);
    }

    public async ValueTask RevokeUrlAsync(string url)
    {
        if (_module is not null && !string.IsNullOrEmpty(url))
            await _module.InvokeVoidAsync("revokeObjectUrl", url);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null) return;
        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone; nothing to clean up.
        }
    }

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/AReport.Blazor/areport.js");
}
