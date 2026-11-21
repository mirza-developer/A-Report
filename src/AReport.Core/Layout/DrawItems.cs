using AReport.Fonts;
using AReport.Imaging;
using AReport.Model;

namespace AReport.Layout;

/// <summary>Renderer-independent drawing primitives. All coordinates are millimetres from the page's top-left corner.</summary>
public abstract record DrawItem;

public sealed record RectItem(double XMm, double YMm, double WidthMm, double HeightMm, RgbColor? Fill, RgbColor? Stroke, double StrokeWidthMm) : DrawItem;

public sealed record LineItem(double X1Mm, double Y1Mm, double X2Mm, double Y2Mm, RgbColor Color, double WidthMm) : DrawItem;

public sealed record TextLine(string Text, double XMm, double BaselineYMm);

/// <summary>Pre-positioned text lines, clipped to the box (X, Y, Width, Height).</summary>
public sealed record TextItem(double XMm, double YMm, double WidthMm, double HeightMm, FontSpec Font, RgbColor Color, IReadOnlyList<TextLine> Lines) : DrawItem;

public sealed record ImageItem(double XMm, double YMm, double WidthMm, double HeightMm, ReportImage Image) : DrawItem;

public sealed class LaidOutPage(double widthMm, double heightMm)
{
    public double WidthMm { get; } = widthMm;
    public double HeightMm { get; } = heightMm;
    public List<DrawItem> Items { get; } = [];
}

public sealed class LaidOutDocument(string title, IReadOnlyList<LaidOutPage> pages, IReadOnlyList<string> warnings)
{
    public string Title { get; } = title;
    public IReadOnlyList<LaidOutPage> Pages { get; } = pages;
    public IReadOnlyList<string> Warnings { get; } = warnings;
}
