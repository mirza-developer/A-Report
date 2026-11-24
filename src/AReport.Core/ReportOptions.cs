using System.Globalization;
using AReport.Fonts;

namespace AReport;

/// <summary>Settings shared by the whole pipeline.</summary>
public sealed class ReportOptions
{
    /// <summary>Folder that holds the <c>.art</c> files (for example <c>wwwroot/reports</c>).</summary>
    public string ReportsDirectory { get; set; } = "reports";

    /// <summary>Culture used to format numbers and dates inside placeholders and table cells.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>When true an unresolved placeholder throws instead of producing a warning.</summary>
    public bool StrictBindings { get; set; }

    /// <summary>Font folders and fallbacks for Persian, Chinese and other non-Latin text.</summary>
    public FontOptions Fonts { get; } = new();

    /// <summary>Deflate-compress PDF content streams (turn off to debug the raw PDF).</summary>
    public bool CompressPdf { get; set; } = true;
}
