using System.Text.Json.Serialization;

namespace AReport.Model;

/// <summary>The in-memory form of an .art file.</summary>
public sealed class ReportDesign
{
    public string Format { get; set; } = ArtFormat.Identifier;

    public int SchemaVersion { get; set; } = ArtFormat.CurrentVersion;

    public string Name { get; set; } = "Untitled report";

    public PageSettings Page { get; set; } = new();

    /// <summary>Variables, data sources and images the report uses (documentation for the designer, defaults at run time).</summary>
    public ReportDictionary Dictionary { get; set; } = new();

    /// <summary>Printed once at the top of the first page, below the page header.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Band? ReportHeader { get; set; }

    /// <summary>Printed at the top of every page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Band? PageHeader { get; set; }

    /// <summary>Printed at the bottom of every page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Band? PageFooter { get; set; }

    /// <summary>Printed once after the last content, on the last page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Band? ReportFooter { get; set; }

    /// <summary>The body: free elements positioned on the page, plus tables and repeaters that flow over pages. Back-to-front order.</summary>
    public List<ReportElement> Elements { get; set; } = [];
}
