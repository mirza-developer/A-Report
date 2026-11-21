using AReport.Layout;

namespace AReport.Pdf;

/// <summary>Turns laid-out pages into a file format. <see cref="PdfRenderer"/> is the built-in implementation.</summary>
public interface IReportRenderer
{
    string ContentType { get; }

    string FileExtension { get; }

    byte[] Render(LaidOutDocument document);
}

/// <summary>Sanity-checks rendered output (the same checks the old PdfExporter performed).</summary>
public interface IOutputValidator
{
    void Validate(byte[] content);
}

public sealed class PdfOutputValidator : IOutputValidator
{
    public void Validate(byte[] content)
    {
        if (content is null || content.Length == 0)
            throw new ReportRenderException("PDF export produced an empty file.");
        if (content.Length < 5 || content[0] != '%' || content[1] != 'P' || content[2] != 'D' || content[3] != 'F' || content[4] != '-')
            throw new ReportRenderException("PDF export did not produce a valid PDF header.");

        var tail = content.AsSpan(Math.Max(0, content.Length - 1024));
        if (tail.IndexOf("%%EOF"u8) < 0)
            throw new ReportRenderException("PDF export did not produce a complete PDF (missing end-of-file marker).");
    }
}
