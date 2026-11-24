using System.Globalization;
using AReport.Barcodes;
using AReport.Data;
using AReport.Fonts;
using AReport.Imaging;
using AReport.Model;
using AReport.Templates;

namespace AReport.Layout;

/// <summary>
/// Turns a design plus data into pages: page header/footer and report header/footer bands, free elements, and repeaters
/// (data bands, tables) that flow over pages with a repeating header and a totals footer.
/// </summary>
public sealed class LayoutEngine : ILayoutEngine
{
    private const double GridWidthMm = 0.2;
    private const double BarcodeTextHeightMm = 3.6;
    private const double Epsilon = 1e-9;

    private readonly ITemplateEngine _templates;
    private readonly IValueResolver _resolver;
    private readonly ReportOptions _options;
    private readonly TimeProvider _time;
    private readonly TextLayouter _text;

    public LayoutEngine(IFontMetrics metrics, ITemplateEngine templates, IValueResolver resolver, ReportOptions options,
        TimeProvider? time = null, FontSystem? fonts = null)
    {
        _templates = templates;
        _resolver = resolver;
        _options = options;
        _time = time ?? TimeProvider.System;
        _text = fonts is null ? new TextLayouter(metrics) : new TextLayouter(fonts);
    }

    public LaidOutDocument Layout(ReportDesign design, ReportData data, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(data);

        var merged = ApplyDictionaryDefaults(design, data);
        string documentTitle = string.IsNullOrWhiteSpace(title) ? design.Name : title;

        // The page count is only known after the first pass; headers and footers may print it, so build again with it.
        int hint = 0;
        LaidOutDocument? document = null;
        for (int pass = 0; pass < 3; pass++)
        {
            var warnings = new DistinctWarnings();
            var pages = new Builder(this, design, merged, warnings, hint).Build();
            document = new LaidOutDocument(documentTitle, pages, warnings.ToList());
            if (pages.Count == hint) break;
            hint = pages.Count;
        }
        return document!;
    }

    private static ReportData ApplyDictionaryDefaults(ReportDesign design, ReportData data)
    {
        var copy = data.Clone();
        foreach (var variable in design.Dictionary.Variables)
        {
            if (string.IsNullOrWhiteSpace(variable.Name) || variable.DefaultValue is null || copy.TryGetVariable(variable.Name, out _)) continue;
            copy.AddVariable(variable.Name, ConvertDefault(variable));
        }
        foreach (var image in design.Dictionary.Images)
        {
            if (string.IsNullOrWhiteSpace(image.Name) || copy.HasImage(image.Name)) continue;
            try
            {
                copy.AddImage(image.Name, Convert.FromBase64String(image.Base64));
            }
            catch (FormatException)
            {
                // an invalid resource is reported when an element uses it
            }
        }
        return copy;
    }

    private static object? ConvertDefault(VariableDefinition variable)
    {
        var text = variable.DefaultValue!;
        return variable.Type switch
        {
            VariableType.Number when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
            VariableType.Date when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) => date,
            VariableType.Boolean when bool.TryParse(text, out var flag) => flag,
            _ => text,
        };
    }

    // =====================================================================================================================

    private sealed class Builder
    {
        private readonly LayoutEngine _e;
        private readonly ReportDesign _design;
        private readonly PageSettings _page;
        private readonly ReportData _data;
        private readonly DistinctWarnings _warn;
        private readonly int _hint;

        private readonly double _left, _top;
        private readonly double _pageHeaderHeight, _pageFooterHeight, _reportHeaderHeight, _reportFooterHeight;
        private readonly double _bodyTop, _bodyBottom;

        private readonly List<LaidOutPage> _pages = [];
        private readonly List<Dictionary<int, List<DrawItem>>> _buckets = [];
        private readonly List<double> _bottoms = [];
        private readonly Dictionary<ImageElement, ReportImage?> _imageCache = [];
        private int _reportFooterPage;
        private double _reportFooterY;

        public Builder(LayoutEngine engine, ReportDesign design, ReportData data, DistinctWarnings warnings, int pageCountHint)
        {
            _e = engine;
            _design = design;
            _page = design.Page;
            _data = data;
            _warn = warnings;
            _hint = pageCountHint;

            _left = _page.MarginLeftMm;
            _top = _page.MarginTopMm;
            _pageHeaderHeight = design.PageHeader?.HeightMm ?? 0;
            _pageFooterHeight = design.PageFooter?.HeightMm ?? 0;
            _reportHeaderHeight = design.ReportHeader?.HeightMm ?? 0;
            _reportFooterHeight = design.ReportFooter?.HeightMm ?? 0;
            _bodyTop = _top + _pageHeaderHeight;
            _bodyBottom = _page.HeightMm - _page.MarginBottomMm - _pageFooterHeight;
        }

        public List<LaidOutPage> Build()
        {
            EnsurePage(0);
            FlowBody();
            PlaceReportFooter();
            return Assemble();
        }

        // ---------- pages ----------

        private void EnsurePage(int index)
        {
            while (_pages.Count <= index)
            {
                _pages.Add(new LaidOutPage(_page.WidthMm, _page.HeightMm));
                _buckets.Add([]);
                _bottoms.Add(_pages.Count == 1 ? _bodyTop + _reportHeaderHeight : _bodyTop);
            }
        }

        private List<DrawItem> Bucket(int page, int element)
        {
            EnsurePage(page);
            if (!_buckets[page].TryGetValue(element, out var list)) _buckets[page][element] = list = [];
            return list;
        }

        private BindingContext Context(int page, object? row = null, string? source = null, int line = 0) =>
            new(_data, page + 1, _hint > 0 ? _hint : 1, _e._time.GetLocalNow(), _e._options.Culture, _e._options.StrictBindings, row, source, line);

        // ---------- body flow ----------

        private static bool IsFlow(ReportElement e) => e is TableElement or RepeaterElement;

        private void FlowBody()
        {
            var elements = _design.Elements;
            var order = Enumerable.Range(0, elements.Count)
                .Where(i => IsFlow(elements[i]) || !elements[i].RepeatOnEveryPage)
                .OrderBy(i => elements[i].Y).ThenBy(i => i)
                .ToList();

            bool anchored = false;
            double anchorDesignBottom = 0, anchorEndY = 0;
            int anchorPage = 0;

            foreach (int index in order)
            {
                var element = elements[index];
                if (IsFlow(element))
                {
                    var repeater = element as RepeaterElement ?? Compile((TableElement)element);
                    int page = anchored ? anchorPage : 0;
                    double y = anchored ? anchorEndY + Math.Max(0, element.Y - anchorDesignBottom) : element.Y;

                    var (endPage, endY) = FlowRepeater(index, repeater, page, y);
                    anchored = true;
                    anchorPage = endPage;
                    anchorEndY = endY;
                    anchorDesignBottom = element.Y + element.Height;
                    continue;
                }

                int targetPage = 0;
                double dy = 0;
                if (anchored && element.Y >= anchorDesignBottom - Epsilon)
                {
                    // Content designed below a repeater moves down with it, onto the page where the data ended.
                    targetPage = anchorPage;
                    double y = anchorEndY + (element.Y - anchorDesignBottom);
                    if (y + element.Height > _bodyBottom + Epsilon && y > _bodyTop + Epsilon)
                    {
                        targetPage++;
                        y = _bodyTop + (element.Y - anchorDesignBottom);
                    }
                    dy = y - element.Y;
                }

                var items = Bucket(targetPage, index);
                var ctx = Context(targetPage);
                EmitElement(items, element, element.X, element.Y + dy, ctx, null, null);
                _bottoms[targetPage] = Math.Max(_bottoms[targetPage], element.Y + dy + element.Height);
            }
        }

        private readonly Dictionary<TableElement, RepeaterElement> _compiled = [];

        private RepeaterElement Compile(TableElement table)
        {
            if (!_compiled.TryGetValue(table, out var repeater)) _compiled[table] = repeater = TableCompiler.Compile(table);
            return repeater;
        }

        private (int Page, double EndY) FlowRepeater(int elementIndex, RepeaterElement rep, int page, double y)
        {
            IReadOnlyList<object?> rows = [];
            if (!_data.TryGetDataSource(rep.DataSource, out rows))
                _warn.Add($"Data source '{rep.DataSource}' was not provided.");

            EnsurePage(page);
            int current = page;
            double cursor = y;

            bool hasHeader = rep.Header.HeightMm > 0 || rep.Header.Elements.Count > 0;
            void EmitHeader()
            {
                var ctx = Context(current, null, rep.DataSource);
                double h = SectionHeight(rep.Header, ctx);
                EmitSection(Bucket(current, elementIndex), rep.Header, rep.X, cursor, ctx, null, h);
                cursor += h;
            }

            if (hasHeader) EmitHeader();

            int line = 0;
            foreach (var row in rows)
            {
                line++;
                var probe = Context(current, row, rep.DataSource, line);
                double rowHeight = SectionHeight(rep.Row, probe);

                if (cursor + rowHeight > _bodyBottom + Epsilon && cursor > _bodyTop + Epsilon)
                {
                    _bottoms[current] = Math.Max(_bottoms[current], cursor);
                    current++;
                    EnsurePage(current);
                    cursor = _bodyTop;
                    if (hasHeader && rep.RepeatHeaderOnEveryPage) EmitHeader();
                }

                var ctx = Context(current, row, rep.DataSource, line);
                string? zebra = line % 2 == 0 ? rep.ZebraColor : null;
                EmitSection(Bucket(current, elementIndex), rep.Row, rep.X, cursor, ctx, zebra, rowHeight);
                cursor += rowHeight;
            }

            if (rep.Footer is { } footer)
            {
                var ctx = Context(current, null, rep.DataSource);
                double h = SectionHeight(footer, ctx);
                if (cursor + h > _bodyBottom + Epsilon && cursor > _bodyTop + Epsilon)
                {
                    _bottoms[current] = Math.Max(_bottoms[current], cursor);
                    current++;
                    EnsurePage(current);
                    cursor = _bodyTop;
                    if (hasHeader && rep.RepeatHeaderOnEveryPage) EmitHeader();
                    ctx = Context(current, null, rep.DataSource);
                }
                EmitSection(Bucket(current, elementIndex), footer, rep.X, cursor, ctx, null, h);
                cursor += h;
            }

            _bottoms[current] = Math.Max(_bottoms[current], cursor);
            return (current, cursor);
        }

        private void PlaceReportFooter()
        {
            _reportFooterPage = _pages.Count - 1;
            if (_design.ReportFooter is null) return;

            double y = Math.Max(_bodyTop, _bottoms[_reportFooterPage]);
            if (y + _reportFooterHeight > _bodyBottom + Epsilon && y > _bodyTop + Epsilon)
            {
                _reportFooterPage++;
                EnsurePage(_reportFooterPage);
                y = _bodyTop;
            }
            _reportFooterY = y;
        }

        // ---------- assembling ----------

        private List<LaidOutPage> Assemble()
        {
            var elements = _design.Elements;
            for (int p = 0; p < _pages.Count; p++)
            {
                var page = _pages[p];
                var ctx = Context(p);

                if (_design.PageHeader is { } pageHeader)
                    EmitBand(page.Items, pageHeader, _top, ctx, "Page header");
                if (p == 0 && _design.ReportHeader is { } reportHeader)
                    EmitBand(page.Items, reportHeader, _bodyTop, ctx, "Report header");
                if (_design.PageFooter is { } pageFooter)
                    EmitBand(page.Items, pageFooter, _page.HeightMm - _page.MarginBottomMm - _pageFooterHeight, ctx, "Page footer");
                if (p == _reportFooterPage && _design.ReportFooter is { } reportFooter)
                    EmitBand(page.Items, reportFooter, _reportFooterY, ctx, "Report footer");

                for (int i = 0; i < elements.Count; i++)
                {
                    var element = elements[i];
                    if (!IsFlow(element) && element.RepeatOnEveryPage)
                        EmitElement(page.Items, element, element.X, element.Y, ctx, null, null);
                    else if (_buckets[p].TryGetValue(i, out var items))
                        page.Items.AddRange(items);
                }
            }
            return _pages;
        }

        private void EmitBand(List<DrawItem> items, Band band, double top, BindingContext ctx, string name)
        {
            double h = band.HeightMm;
            foreach (var element in band.Elements)
            {
                if (IsFlow(element))
                {
                    _warn.Add($"{name}: '{element.Label}' is a table or data band, which can only be placed in the body.");
                    continue;
                }
                EmitElement(items, element, _left + element.X, top + element.Y, ctx, null, ForcedHeight(element, band.HeightMm, h, null));
            }
        }

        // ---------- sections (repeater header / row / footer) ----------

        private double SectionHeight(Band band, BindingContext ctx)
        {
            double height = band.HeightMm;
            foreach (var element in band.Elements.OfType<TextElement>().Where(t => t.CanGrow))
                height = Math.Max(height, element.Y + GrownTextHeight(element, ctx));
            return height;
        }

        private void EmitSection(List<DrawItem> items, Band band, double baseX, double baseY, BindingContext ctx, string? rowFill, double sectionHeight)
        {
            foreach (var element in band.Elements)
            {
                if (IsFlow(element))
                {
                    _warn.Add($"'{element.Label}' is a table or data band, which cannot be nested inside another band.");
                    continue;
                }
                double? height = ForcedHeight(element, band.HeightMm, sectionHeight, ctx);
                EmitElement(items, element, baseX + element.X, baseY + element.Y, ctx, rowFill, height);
            }
        }

        /// <summary>The height an element takes when its section grew: text that can grow, or elements stretched to the section.</summary>
        private double? ForcedHeight(ReportElement element, double nominalSectionHeight, double actualSectionHeight, BindingContext? ctx)
        {
            double height = element.Height;
            if (element is TextElement { CanGrow: true } text && ctx is not null)
                height = Math.Max(height, GrownTextHeight(text, ctx));
            if (element.GrowToHeight && element.Y + element.Height >= nominalSectionHeight - 0.01)
                height = Math.Max(height, actualSectionHeight - element.Y);
            return Math.Abs(height - element.Height) < Epsilon ? null : height;
        }

        private double GrownTextHeight(TextElement t, BindingContext ctx)
        {
            string text = _e._templates.Render(t.Text, ctx, new DistinctWarnings());
            var block = _e._text.Arrange(text, FontOf(t), 0, 0, t.Width, t.Height, t.HorizontalAlignment, VAlign.Top, t.WordWrap,
                t.PaddingMm, t.Direction, t.Digits);
            return Math.Max(t.Height, block.ContentHeightMm + 2 * t.PaddingMm - 0.0);
        }

        private static FontSpec FontOf(TextElement t) => new(t.FontFamily, t.FontSize, t.Bold, t.Italic);

        // ---------- single elements ----------

        private void EmitElement(List<DrawItem> items, ReportElement element, double x, double y, BindingContext ctx, string? rowFill, double? height)
        {
            double h = height ?? element.Height;
            switch (element)
            {
                case TextElement t:
                    EmitText(items, t, x, y, h, ctx, rowFill);
                    break;
                case RectangleElement r:
                    items.Add(new RectItem(x, y, r.Width, h, OptionalColor(r.FillColor, r.Label), OptionalColor(r.StrokeColor, r.Label), r.StrokeWidthMm));
                    break;
                case ShapeElement s:
                    items.Add(new ShapeItem(s.Shape, x, y, s.Width, h, OptionalColor(s.FillColor, s.Label), OptionalColor(s.StrokeColor, s.Label), s.StrokeWidthMm, s.CornerRadiusMm));
                    break;
                case LineElement l:
                    items.Add(new LineItem(x, y, x + l.Width, y + l.Height, ColorOf(l.StrokeColor, RgbColor.Black, l.Label), l.StrokeWidthMm));
                    break;
                case ImageElement i:
                    EmitImage(items, i, x, y, h);
                    break;
                case BarcodeElement b:
                    EmitBarcode(items, b, x, y, h, ctx);
                    break;
            }
        }

        private void EmitText(List<DrawItem> items, TextElement t, double x, double y, double h, BindingContext ctx, string? rowFill)
        {
            string text = _e._templates.Render(t.Text, ctx, _warn);
            var font = FontOf(t);

            double rotation = ((t.Rotation % 360) + 360) % 360;
            bool swapped = Math.Abs(rotation - 90) < Epsilon || Math.Abs(rotation - 270) < Epsilon;
            double boxW = swapped ? h : t.Width, boxH = swapped ? t.Width : h;
            double cx = x + t.Width / 2, cy = y + h / 2;
            double x0 = cx - boxW / 2, y0 = cy - boxH / 2;

            var block = _e._text.Arrange(text, font, x0, y0, boxW, boxH, t.HorizontalAlignment, t.VerticalAlignment, t.WordWrap,
                t.PaddingMm, t.Direction, t.Digits, _warn.Add);
            if (block.ContentHeightMm > boxH + 0.01)
                _warn.Add($"Text '{t.Label}' does not fit in its box and is clipped.");

            var fill = OptionalColor(t.FillColor ?? rowFill, t.Label);
            var border = t.Borders != BorderSides.None ? ColorOf(t.BorderColor, RgbColor.Black, t.Label) : (RgbColor?)null;
            if (t.Borders == BorderSides.All)
            {
                items.Add(new RectItem(x, y, t.Width, h, fill, border, t.BorderWidthMm));
            }
            else
            {
                if (fill is not null) items.Add(new RectItem(x, y, t.Width, h, fill, null, 0));
                if (border is { } c)
                {
                    if (t.Borders.HasFlag(BorderSides.Left)) items.Add(new LineItem(x, y, x, y + h, c, t.BorderWidthMm));
                    if (t.Borders.HasFlag(BorderSides.Top)) items.Add(new LineItem(x, y, x + t.Width, y, c, t.BorderWidthMm));
                    if (t.Borders.HasFlag(BorderSides.Right)) items.Add(new LineItem(x + t.Width, y, x + t.Width, y + h, c, t.BorderWidthMm));
                    if (t.Borders.HasFlag(BorderSides.Bottom)) items.Add(new LineItem(x, y + h, x + t.Width, y + h, c, t.BorderWidthMm));
                }
            }

            items.Add(new TextItem(x0, y0, boxW, boxH, font, ColorOf(t.Color, RgbColor.Black, t.Label), block.Lines, rotation));
        }

        private void EmitImage(List<DrawItem> items, ImageElement element, double x, double y, double h)
        {
            if (!_imageCache.TryGetValue(element, out var image))
            {
                image = ResolveImage(element);
                _imageCache[element] = image;
            }
            if (image is null) return;

            double w = element.Width;
            double ix = x, iy = y, iw = w, ih = h;
            if (element.Fit == ImageFit.Contain && w > 0 && h > 0)
            {
                double ratio = (double)image.WidthPx / image.HeightPx;
                if (ratio > w / h)
                {
                    ih = w / ratio;
                    iy += (h - ih) / 2;
                }
                else
                {
                    iw = h * ratio;
                    ix += (w - iw) / 2;
                }
            }
            items.Add(new ImageItem(ix, iy, iw, ih, image));
        }

        private ReportImage? ResolveImage(ImageElement element)
        {
            if (!string.IsNullOrWhiteSpace(element.ImageKey))
            {
                if (_data.TryGetImage(element.ImageKey, out var supplied)) return supplied;
                if (_data.TryGetImageError(element.ImageKey, out var error)) _warn.Add(error);
                else if (string.IsNullOrWhiteSpace(element.EmbeddedImageBase64))
                    _warn.Add($"Image '{element.ImageKey}' was not provided.");
            }

            if (!string.IsNullOrWhiteSpace(element.EmbeddedImageBase64))
            {
                try
                {
                    var bytes = Convert.FromBase64String(element.EmbeddedImageBase64);
                    if (ImageReader.TryRead(bytes, out var embedded, out var error)) return embedded;
                    _warn.Add($"Image '{element.Label}': {error}");
                }
                catch (FormatException)
                {
                    _warn.Add($"Image '{element.Label}' has invalid embedded data.");
                }
            }
            return null;
        }

        private void EmitBarcode(List<DrawItem> items, BarcodeElement b, double x, double y, double h, BindingContext ctx)
        {
            string content = _e._templates.Render(b.Content, ctx, _warn);
            if (content.Length == 0)
            {
                _warn.Add($"Barcode '{b.Label}' has no content.");
                return;
            }

            BarcodeMatrix matrix;
            try
            {
                matrix = b.Symbology switch
                {
                    BarcodeSymbology.QrCode => QrEncoder.Encode(content, Enum.TryParse<QrEccLevel>(b.ErrorCorrection, true, out var level) ? level : QrEccLevel.Medium),
                    BarcodeSymbology.DataMatrix => DataMatrixEncoder.Encode(content),
                    _ => Code128Encoder.Encode(content),
                };
            }
            catch (ArgumentException ex)
            {
                _warn.Add($"Barcode '{b.Label}': {ex.Message}");
                return;
            }

            var fore = ColorOf(b.ForeColor, RgbColor.Black, b.Label);
            var back = OptionalColor(b.BackColor, b.Label);
            if (back is not null) items.Add(new RectItem(x, y, b.Width, h, back, null, 0));

            double quiet = Math.Max(0, b.QuietZoneModules);
            if (b.Symbology == BarcodeSymbology.Code128)
            {
                double textHeight = b.ShowText ? BarcodeTextHeightMm : 0;
                double barsHeight = Math.Max(1, h - textHeight);
                double module = b.Width / (matrix.Columns + 2 * quiet);
                items.Add(new BarcodeItem(x + quiet * module, y, matrix.Columns * module, barsHeight, matrix, fore));
                if (b.ShowText)
                {
                    var font = new FontSpec("Helvetica", 8, false, false);
                    var block = _e._text.Arrange(content, font, x, y + barsHeight, b.Width, textHeight, HAlign.Center, VAlign.Middle, false, 0,
                        TextDirection.LeftToRight, DigitStyle.Latin, _warn.Add);
                    items.Add(new TextItem(x, y + barsHeight, b.Width, textHeight, font, fore, block.Lines));
                }
                return;
            }

            double moduleSize = Math.Min(b.Width / (matrix.Columns + 2 * quiet), h / (matrix.Rows + 2 * quiet));
            double symbolW = matrix.Columns * moduleSize, symbolH = matrix.Rows * moduleSize;
            items.Add(new BarcodeItem(x + (b.Width - symbolW) / 2, y + (h - symbolH) / 2, symbolW, symbolH, matrix, fore));
        }

        // ---------- colours ----------

        private RgbColor ColorOf(string? value, RgbColor fallback, string owner)
        {
            if (RgbColor.TryParse(value, out var color)) return color;
            _warn.Add($"'{owner}' has an invalid colour '{value}'.");
            return fallback;
        }

        private RgbColor? OptionalColor(string? value, string owner) =>
            string.IsNullOrWhiteSpace(value) ? null : ColorOf(value, RgbColor.Black, owner);
    }
}
