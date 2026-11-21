using AReport.Data;
using AReport.Fonts;
using AReport.Imaging;
using AReport.Model;
using AReport.Templates;

namespace AReport.Layout;

public sealed class LayoutEngine : ILayoutEngine
{
    private const double CellPaddingMm = 1.5;
    private const double GridWidthMm = 0.2;

    private readonly ITemplateEngine _templates;
    private readonly IValueResolver _resolver;
    private readonly ReportOptions _options;
    private readonly TimeProvider _time;
    private readonly TextLayouter _text;

    public LayoutEngine(IFontMetrics metrics, ITemplateEngine templates, IValueResolver resolver, ReportOptions options, TimeProvider? time = null)
    {
        _templates = templates;
        _resolver = resolver;
        _options = options;
        _time = time ?? TimeProvider.System;
        _text = new TextLayouter(metrics);
    }

    private sealed record TablePlan(IReadOnlyList<object?> Rows, IReadOnlyList<(int Start, int Count)> Pages, double[] ColumnWidths);

    public LaidOutDocument Layout(ReportDesign design, ReportData data, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(data);

        var warnings = new DistinctWarnings();
        var page = design.Page;

        var plans = new Dictionary<TableElement, TablePlan>();
        foreach (var table in design.Elements.OfType<TableElement>())
            plans[table] = Plan(table, page, data, warnings);

        int pageCount = Math.Max(1, plans.Values.Select(p => p.Pages.Count).DefaultIfEmpty(1).Max());
        var images = new Dictionary<ImageElement, ReportImage?>();
        var now = _time.GetLocalNow();
        var pages = new List<LaidOutPage>(pageCount);

        for (int p = 0; p < pageCount; p++)
        {
            var laid = new LaidOutPage(page.WidthMm, page.HeightMm);
            var context = new BindingContext(data, p + 1, pageCount, now, _options.Culture, _options.StrictBindings);

            foreach (var element in design.Elements)
            {
                switch (element)
                {
                    case TableElement table:
                        if (p < plans[table].Pages.Count)
                            EmitTable(table, plans[table], p, page, laid, warnings);
                        break;
                    case var e when p == 0 || e.RepeatOnEveryPage:
                        EmitElement(e, context, data, images, laid, warnings);
                        break;
                }
            }
            pages.Add(laid);
        }

        return new LaidOutDocument(string.IsNullOrWhiteSpace(title) ? design.Name : title, pages, warnings.ToList());
    }

    // ---------- simple elements ----------

    private void EmitElement(ReportElement element, BindingContext context, ReportData data,
        Dictionary<ImageElement, ReportImage?> imageCache, LaidOutPage laid, DistinctWarnings warnings)
    {
        switch (element)
        {
            case TextElement t:
            {
                string text = _templates.Render(t.Text, context, warnings);
                var font = new FontSpec(t.FontFamily, t.FontSize, t.Bold, t.Italic);
                var block = _text.Arrange(text, font, t.X, t.Y, t.Width, t.Height, t.HorizontalAlignment, t.VerticalAlignment, t.WordWrap);
                CheckEncoding(text, t.Label, warnings);
                if (block.ContentHeightMm > t.Height + 0.01)
                    warnings.Add($"Text '{t.Label}' does not fit in its box and is clipped.");
                laid.Items.Add(new TextItem(t.X, t.Y, t.Width, t.Height, font, Color(t.Color, RgbColor.Black, t.Label, warnings), block.Lines));
                break;
            }
            case RectangleElement r:
                laid.Items.Add(new RectItem(r.X, r.Y, r.Width, r.Height,
                    OptionalColor(r.FillColor, r.Label, warnings), OptionalColor(r.StrokeColor, r.Label, warnings), r.StrokeWidthMm));
                break;
            case LineElement l:
                laid.Items.Add(new LineItem(l.X, l.Y, l.X + l.Width, l.Y + l.Height, Color(l.StrokeColor, RgbColor.Black, l.Label, warnings), l.StrokeWidthMm));
                break;
            case ImageElement i:
                EmitImage(i, data, imageCache, laid, warnings);
                break;
        }
    }

    private static void EmitImage(ImageElement element, ReportData data, Dictionary<ImageElement, ReportImage?> cache,
        LaidOutPage laid, DistinctWarnings warnings)
    {
        if (!cache.TryGetValue(element, out var image))
        {
            image = ResolveImage(element, data, warnings);
            cache[element] = image;
        }
        if (image is null) return;

        double x = element.X, y = element.Y, w = element.Width, h = element.Height;
        if (element.Fit == ImageFit.Contain && element.Width > 0 && element.Height > 0)
        {
            double imageRatio = (double)image.WidthPx / image.HeightPx;
            if (imageRatio > element.Width / element.Height)
            {
                h = element.Width / imageRatio;
                y += (element.Height - h) / 2;
            }
            else
            {
                w = element.Height * imageRatio;
                x += (element.Width - w) / 2;
            }
        }
        laid.Items.Add(new ImageItem(x, y, w, h, image));
    }

    private static ReportImage? ResolveImage(ImageElement element, ReportData data, DistinctWarnings warnings)
    {
        if (!string.IsNullOrWhiteSpace(element.ImageKey))
        {
            if (data.TryGetImage(element.ImageKey, out var supplied)) return supplied;
            if (data.TryGetImageError(element.ImageKey, out var error)) warnings.Add(error);
            else if (string.IsNullOrWhiteSpace(element.EmbeddedImageBase64))
                warnings.Add($"Image '{element.ImageKey}' was not provided.");
        }

        if (!string.IsNullOrWhiteSpace(element.EmbeddedImageBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(element.EmbeddedImageBase64);
                if (ImageReader.TryRead(bytes, out var embedded, out var error)) return embedded;
                warnings.Add($"Image '{element.Label}': {error}");
            }
            catch (FormatException)
            {
                warnings.Add($"Image '{element.Label}' has invalid embedded data.");
            }
        }
        return null;
    }

    // ---------- tables ----------

    private TablePlan Plan(TableElement table, PageSettings page, ReportData data, DistinctWarnings warnings)
    {
        IReadOnlyList<object?> rows = [];
        if (!data.TryGetDataSource(table.DataSource, out rows))
            warnings.Add($"Data source '{table.DataSource}' was not provided.");

        double totalWeight = table.Columns.Sum(c => c.Weight);
        double[] widths = table.Columns.Select(c => totalWeight > 0 ? table.Width * c.Weight / totalWeight : 0).ToArray();

        int FitRows(double top) => Math.Max(0, (int)Math.Floor((page.ContentBottomMm - top - table.HeaderHeightMm + 1e-9) / table.RowHeightMm));

        int firstCapacity = FitRows(table.Y);
        int nextCapacity = Math.Max(1, FitRows(page.MarginTopMm));

        var pages = new List<(int, int)>();
        int index = 0;
        bool first = true;
        while (first || index < rows.Count)
        {
            int take = Math.Min(first ? firstCapacity : nextCapacity, rows.Count - index);
            pages.Add((index, take));
            index += take;
            first = false;
        }
        return new TablePlan(rows, pages, widths);
    }

    private void EmitTable(TableElement table, TablePlan plan, int pageIndex, PageSettings page, LaidOutPage laid, DistinctWarnings warnings)
    {
        var (start, count) = plan.Pages[pageIndex];
        double top = pageIndex == 0 ? table.Y : page.MarginTopMm;
        var font = new FontSpec(table.FontFamily, table.FontSize, false, false);
        var headerFont = font with { Bold = true };
        var grid = Color(table.GridColor, RgbColor.Black, table.Label, warnings);
        var headerFill = Color(table.HeaderFillColor, RgbColor.White, table.Label, warnings);
        var headerText = Color(table.HeaderTextColor, RgbColor.Black, table.Label, warnings);
        var bodyText = Color(table.TextColor, RgbColor.Black, table.Label, warnings);
        var zebra = OptionalColor(table.ZebraColor, table.Label, warnings);

        double x = table.X;
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var column = table.Columns[c];
            double w = plan.ColumnWidths[c];
            laid.Items.Add(new RectItem(x, top, w, table.HeaderHeightMm, headerFill, grid, GridWidthMm));
            AddCell(laid, column.Header, headerFont, headerText, x, top, w, table.HeaderHeightMm, column.Alignment, table.Label, warnings);
            x += w;
        }

        for (int r = 0; r < count; r++)
        {
            int rowIndex = start + r;
            object? row = plan.Rows[rowIndex];
            double y = top + table.HeaderHeightMm + r * table.RowHeightMm;
            RgbColor? fill = zebra is not null && rowIndex % 2 == 1 ? zebra : null;

            x = table.X;
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var column = table.Columns[c];
                double w = plan.ColumnWidths[c];
                laid.Items.Add(new RectItem(x, y, w, table.RowHeightMm, fill, grid, GridWidthMm));
                AddCell(laid, CellText(column, row, rowIndex, table, warnings), font, bodyText, x, y, w, table.RowHeightMm, column.Alignment, table.Label, warnings);
                x += w;
            }
        }
    }

    private string CellText(TableColumn column, object? row, int rowIndex, TableElement table, DistinctWarnings warnings)
    {
        if (column.Binding == "#")
            return ValueFormatter.Format(rowIndex + 1, column.Format, _options.Culture);

        if (!_resolver.TryResolve(row, column.Binding, out var value))
        {
            warnings.Add($"Table '{table.Label}': column binding '{column.Binding}' was not found.");
            return "";
        }
        return ValueFormatter.Format(value, column.Format, _options.Culture);
    }

    private void AddCell(LaidOutPage laid, string text, FontSpec font, RgbColor color,
        double x, double y, double w, double h, HAlign align, string owner, DistinctWarnings warnings)
    {
        CheckEncoding(text, owner, warnings);
        var block = _text.Arrange(text, font, x, y, w, h, align, VAlign.Middle, wrap: false, CellPaddingMm);
        laid.Items.Add(new TextItem(x, y, w, h, font, color, block.Lines));
    }

    // ---------- helpers ----------

    private static void CheckEncoding(string text, string owner, DistinctWarnings warnings)
    {
        if (!text.All(c => c is '\n' or '\r' || WinAnsi.IsSupported(c)))
            warnings.Add($"'{owner}' contains characters outside the Latin (WinAnsi) range; they are drawn as '?'.");
    }

    private static RgbColor Color(string? value, RgbColor fallback, string owner, DistinctWarnings warnings)
    {
        if (RgbColor.TryParse(value, out var color)) return color;
        warnings.Add($"'{owner}' has an invalid colour '{value}'.");
        return fallback;
    }

    private static RgbColor? OptionalColor(string? value, string owner, DistinctWarnings warnings)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Color(value, RgbColor.Black, owner, warnings);
    }
}
