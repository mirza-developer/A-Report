namespace AReport;

/// <summary>Base type of every error raised on purpose by A-Report.</summary>
public class ReportException : Exception
{
    public ReportException(string message) : base(message) { }

    public ReportException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The file is not a valid or supported .art document.</summary>
public sealed class ArtFormatException : ReportException
{
    public ArtFormatException(string message) : base(message) { }

    public ArtFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The requested report design does not exist.</summary>
public sealed class ReportNotFoundException : ReportException
{
    public ReportNotFoundException(string reportName)
        : base($"Report '{reportName}' was not found.") => ReportName = reportName;

    public string ReportName { get; }
}

/// <summary>The design is well-formed but cannot be rendered (see <see cref="Issues"/>).</summary>
public sealed class ReportDesignException : ReportException
{
    public ReportDesignException(IReadOnlyList<string> issues)
        : base("The report design is invalid: " + string.Join(" ", issues)) => Issues = issues;

    public IReadOnlyList<string> Issues { get; }
}

/// <summary>A placeholder could not be resolved and <c>StrictBindings</c> is on.</summary>
public sealed class ReportBindingException : ReportException
{
    public ReportBindingException(string message) : base(message) { }
}

/// <summary>The renderer produced unusable output.</summary>
public sealed class ReportRenderException : ReportException
{
    public ReportRenderException(string message) : base(message) { }
}
