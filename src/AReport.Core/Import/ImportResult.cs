using AReport.Model;

namespace AReport.Import;

public enum ImportSeverity
{
    /// <summary>Something was converted in a way worth knowing about, but nothing is lost.</summary>
    Info,

    /// <summary>Something could not be converted exactly; the report needs a look.</summary>
    Warning,
}

public sealed record ImportMessage(ImportSeverity Severity, string Text);

/// <summary>The converted design plus everything the converter could not carry over exactly.</summary>
public sealed class ImportResult(ReportDesign design, IReadOnlyList<ImportMessage> messages)
{
    public ReportDesign Design { get; } = design;

    public IReadOnlyList<ImportMessage> Messages { get; } = messages;

    public bool HasWarnings => Messages.Any(m => m.Severity == ImportSeverity.Warning);
}
