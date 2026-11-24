namespace AReport.Model;

/// <summary>
/// A horizontal strip: report header/footer, page header/footer, or a section of a repeater (header, row, footer).
/// Element coordinates are millimetres from the band's top-left corner (for page bands: the left margin).
/// </summary>
public sealed class Band
{
    public double HeightMm { get; set; } = 15;

    public List<ReportElement> Elements { get; set; } = [];
}
