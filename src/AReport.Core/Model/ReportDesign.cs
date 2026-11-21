namespace AReport.Model;

/// <summary>The in-memory form of an .art file.</summary>
public sealed class ReportDesign
{
    public string Format { get; set; } = ArtFormat.Identifier;

    public int SchemaVersion { get; set; } = ArtFormat.CurrentVersion;

    public string Name { get; set; } = "Untitled report";

    public PageSettings Page { get; set; } = new();

    /// <summary>Back-to-front drawing order.</summary>
    public List<ReportElement> Elements { get; set; } = [];
}
