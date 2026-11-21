namespace AReport;

/// <summary>Unit conversions. The .art format stores every length in millimetres; PDF uses points.</summary>
public static class Units
{
    public const double PointsPerMm = 72.0 / 25.4;

    public static double MmToPt(double mm) => mm * PointsPerMm;

    public static double PtToMm(double pt) => pt / PointsPerMm;
}
