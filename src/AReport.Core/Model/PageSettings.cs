using System.Text.Json.Serialization;

namespace AReport.Model;

public sealed class PageSettings
{
    public double WidthMm { get; set; } = 210;
    public double HeightMm { get; set; } = 297;
    public double MarginLeftMm { get; set; } = 10;
    public double MarginTopMm { get; set; } = 10;
    public double MarginRightMm { get; set; } = 10;
    public double MarginBottomMm { get; set; } = 10;

    [JsonIgnore]
    public double ContentBottomMm => HeightMm - MarginBottomMm;

    public static PageSettings A4Portrait() => new();

    public static PageSettings A4Landscape() => new() { WidthMm = 297, HeightMm = 210 };
}
