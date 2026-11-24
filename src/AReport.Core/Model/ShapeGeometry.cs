namespace AReport.Model;

/// <summary>Outlines of the polygon shapes, as points in a unit square (0..1 from the top-left corner).</summary>
public static class ShapeGeometry
{
    /// <summary>Polygon outline, or null for shapes drawn with curves (rectangle, rounded rectangle, ellipse).</summary>
    public static (double X, double Y)[]? Polygon(ShapeKind kind) => kind switch
    {
        ShapeKind.Triangle => [(0.5, 0), (1, 1), (0, 1)],
        ShapeKind.RightTriangle => [(0, 0), (1, 1), (0, 1)],
        ShapeKind.Diamond => [(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)],
        ShapeKind.Hexagon => [(0.25, 0), (0.75, 0), (1, 0.5), (0.75, 1), (0.25, 1), (0, 0.5)],
        ShapeKind.Pentagon => Regular(5, 0),
        ShapeKind.Star => Star(5),
        ShapeKind.ArrowRight => [(0, 0.3), (0.6, 0.3), (0.6, 0), (1, 0.5), (0.6, 1), (0.6, 0.7), (0, 0.7)],
        ShapeKind.ArrowLeft => [(1, 0.3), (0.4, 0.3), (0.4, 0), (0, 0.5), (0.4, 1), (0.4, 0.7), (1, 0.7)],
        ShapeKind.ArrowUp => [(0.3, 1), (0.3, 0.4), (0, 0.4), (0.5, 0), (1, 0.4), (0.7, 0.4), (0.7, 1)],
        ShapeKind.ArrowDown => [(0.3, 0), (0.3, 0.6), (0, 0.6), (0.5, 1), (1, 0.6), (0.7, 0.6), (0.7, 0)],
        _ => null,
    };

    private static (double, double)[] Regular(int sides, double rotation) =>
        Fit(Enumerable.Range(0, sides).Select(i =>
        {
            double angle = -Math.PI / 2 + rotation + 2 * Math.PI * i / sides;
            return (Math.Cos(angle), Math.Sin(angle));
        }).ToArray());

    private static (double, double)[] Star(int points) =>
        Fit(Enumerable.Range(0, points * 2).Select(i =>
        {
            double angle = -Math.PI / 2 + Math.PI * i / points;
            double radius = i % 2 == 0 ? 1 : 0.4;
            return (radius * Math.Cos(angle), radius * Math.Sin(angle));
        }).ToArray());

    /// <summary>Scales and moves points so their bounding box becomes the unit square.</summary>
    private static (double, double)[] Fit((double X, double Y)[] points)
    {
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        return points.Select(p => ((p.X - minX) / (maxX - minX), (p.Y - minY) / (maxY - minY))).ToArray();
    }
}
