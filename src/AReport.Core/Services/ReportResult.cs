namespace AReport.Services;

public sealed class ReportResult(byte[] content, string fileName, string contentType, int pageCount, IReadOnlyList<string> warnings)
{
    public byte[] Content { get; } = content;
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public int PageCount { get; } = pageCount;

    /// <summary>Non-fatal problems (unresolved placeholders, missing images…).</summary>
    public IReadOnlyList<string> Warnings { get; } = warnings;

    /// <summary>A new read-only stream positioned at 0.</summary>
    public MemoryStream ToStream() => new(Content, writable: false);
}
