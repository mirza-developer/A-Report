using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AReport.Fonts;
using AReport.Model;

namespace AReport.Designer.Views;

/// <summary>
/// Draws the report page at true size and lets the user select, move and resize elements.
/// All editing goes through <see cref="DesignerViewModel"/> (one undo step per gesture).
/// </summary>
public sealed class DesignSurface : FrameworkElement
{
    private const double DipPerMm = 96.0 / 25.4;
    private const double HitTolerancePx = 4;
    private const double MinSizeMm = 1;

    private enum DragMode { None, Move, Resize }

    private readonly Dictionary<string, Brush> _brushCache = [];
    private readonly Dictionary<string, BitmapSource?> _imageCache = [];

    private DesignerViewModel? _vm;
    private DragMode _mode;
    private int _handle;
    private Point _startMm;
    private Rect _startRect;
    private ReportElement? _dragged;

    public DesignSurface()
    {
        Focusable = true;
        FocusVisualStyle = null;
        DataContextChanged += OnDataContextChanged;
        GotKeyboardFocus += (_, _) => InvalidateVisual();
        LostKeyboardFocus += (_, _) => InvalidateVisual();
    }

    private double Scale => (_vm?.Zoom ?? 1) * DipPerMm;

    private double Padding => (double)FindResource("Space.6");

    private Brush Res(string key) => (Brush)FindResource(key);

    // ---------- plumbing ----------

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.Changed -= OnViewModelChanged;
        _vm = e.NewValue as DesignerViewModel;
        if (_vm is not null) _vm.Changed += OnViewModelChanged;
        OnViewModelChanged(this, EventArgs.Empty);
    }

    private void OnViewModelChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_vm is null) return new Size(0, 0);
        var page = _vm.Design.Page;
        return new Size(page.WidthMm * Scale + 2 * Padding, page.HeightMm * Scale + 2 * Padding);
    }

    // ---------- drawing ----------

    protected override void OnRender(DrawingContext dc)
    {
        if (_vm is null) return;

        double scale = Scale, pad = Padding;
        var page = _vm.Design.Page;
        var pageRect = new Rect(pad, pad, page.WidthMm * scale, page.HeightMm * scale);

        var shadowOffset = (double)FindResource("Space.1") / 2;
        dc.DrawRectangle(Res("Brush.Border"), null, new Rect(pageRect.X + shadowOffset, pageRect.Y + shadowOffset, pageRect.Width, pageRect.Height));
        dc.DrawRectangle(Res("Brush.Surface"), new Pen(Res("Brush.BorderStrong"), 1), pageRect);

        var guide = new Pen(Res("Brush.Guide"), 1) { DashStyle = DashStyles.Dash };
        dc.DrawRectangle(null, guide, new Rect(
            pad + page.MarginLeftMm * scale, pad + page.MarginTopMm * scale,
            Math.Max(0, (page.WidthMm - page.MarginLeftMm - page.MarginRightMm) * scale),
            Math.Max(0, (page.HeightMm - page.MarginTopMm - page.MarginBottomMm) * scale)));

        foreach (var element in _vm.Design.Elements)
            DrawElement(dc, element);

        if (_vm.SelectedElement is { } selected)
            DrawSelection(dc, selected);

        if (IsKeyboardFocused)
            dc.DrawRectangle(null, new Pen(Res("Brush.Selection"), 2), pageRect);
    }

    private Rect ViewRect(ReportElement e) =>
        new(Padding + e.X * Scale, Padding + e.Y * Scale, Math.Max(0, e.Width) * Scale, Math.Max(0, e.Height) * Scale);

    private void DrawElement(DrawingContext dc, ReportElement element)
    {
        var rect = ViewRect(element);
        switch (element)
        {
            case RectangleElement r:
                dc.DrawRectangle(Optional(r.FillColor), Stroke(r.StrokeColor, r.StrokeWidthMm), rect);
                break;

            case LineElement l:
                dc.DrawLine(Stroke(l.StrokeColor, l.StrokeWidthMm) ?? new Pen(Res("Brush.TextPrimary"), 1), rect.TopLeft, rect.BottomRight);
                break;

            case TextElement t:
                dc.PushClip(new RectangleGeometry(rect));
                DrawText(dc, t.Text, t.FontFamily, t.FontSize, t.Bold, t.Italic, Color(t.Color), rect,
                    t.HorizontalAlignment, t.VerticalAlignment, t.WordWrap);
                dc.Pop();
                break;

            case ImageElement i:
                DrawImage(dc, i, rect);
                break;

            case TableElement table:
                DrawTable(dc, table, rect);
                break;
        }
    }

    private void DrawText(DrawingContext dc, string text, string family, double sizePt, bool bold, bool italic,
        Brush brush, Rect box, HAlign horizontal, VAlign vertical, bool wrap)
    {
        double inset = Scale; // 1 mm of padding, like the PDF layout
        var typeface = new Typeface(new FontFamily(FamilyName(family)), italic ? FontStyles.Italic : FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
            Math.Max(1, sizePt * 96.0 / 72.0 * (_vm?.Zoom ?? 1)), brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, box.Width - 2 * inset),
            TextAlignment = horizontal switch
            {
                HAlign.Center => TextAlignment.Center,
                HAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };
        if (!wrap) formatted.MaxLineCount = 1;

        double y = vertical switch
        {
            VAlign.Middle => box.Y + (box.Height - formatted.Height) / 2,
            VAlign.Bottom => box.Bottom - inset - formatted.Height,
            _ => box.Y + inset,
        };
        dc.DrawText(formatted, new Point(box.X + inset, y));
    }

    private void DrawImage(DrawingContext dc, ImageElement element, Rect rect)
    {
        var bitmap = LoadBitmap(element);
        if (bitmap is null)
        {
            var dash = new Pen(Res("Brush.BorderStrong"), 1) { DashStyle = DashStyles.Dash };
            dc.DrawRectangle(Res("Brush.Background"), dash, rect);
            dc.DrawLine(dash, rect.TopLeft, rect.BottomRight);
            dc.DrawLine(dash, rect.TopRight, rect.BottomLeft);
            dc.PushClip(new RectangleGeometry(rect));
            DrawText(dc, string.IsNullOrWhiteSpace(element.ImageKey) ? "Image" : "Image: " + element.ImageKey, "Helvetica", 8, false, false,
                Res("Brush.TextSecondary"), rect, HAlign.Center, VAlign.Middle, true);
            dc.Pop();
            return;
        }

        var target = rect;
        if (element.Fit == ImageFit.Contain && rect.Width > 0 && rect.Height > 0)
        {
            double ratio = (double)bitmap.PixelWidth / bitmap.PixelHeight;
            if (ratio > rect.Width / rect.Height)
            {
                double h = rect.Width / ratio;
                target = new Rect(rect.X, rect.Y + (rect.Height - h) / 2, rect.Width, h);
            }
            else
            {
                double w = rect.Height * ratio;
                target = new Rect(rect.X + (rect.Width - w) / 2, rect.Y, w, rect.Height);
            }
        }
        dc.DrawImage(bitmap, target);
    }

    private void DrawTable(DrawingContext dc, TableElement table, Rect rect)
    {
        double totalWeight = table.Columns.Sum(c => Math.Max(0, c.Weight));
        if (totalWeight <= 0 || table.Columns.Count == 0)
        {
            dc.DrawRectangle(null, new Pen(Res("Brush.BorderStrong"), 1), rect);
            return;
        }

        var grid = Stroke(table.GridColor, 0.2) ?? new Pen(Res("Brush.BorderStrong"), 1);
        double headerH = table.HeaderHeightMm * Scale, rowH = Math.Max(1, table.RowHeightMm * Scale);
        int rows = (int)Math.Clamp(Math.Floor((rect.Height - headerH) / rowH), 0, 30);

        double x = rect.X;
        foreach (var column in table.Columns)
        {
            double w = rect.Width * Math.Max(0, column.Weight) / totalWeight;
            var header = new Rect(x, rect.Y, w, headerH);
            dc.DrawRectangle(Optional(table.HeaderFillColor), grid, header);
            dc.PushClip(new RectangleGeometry(header));
            DrawText(dc, column.Header, table.FontFamily, table.FontSize, true, false, Color(table.HeaderTextColor), header, column.Alignment, VAlign.Middle, false);
            dc.Pop();

            for (int r = 0; r < rows; r++)
            {
                var cell = new Rect(x, rect.Y + headerH + r * rowH, w, rowH);
                dc.DrawRectangle(r % 2 == 1 ? Optional(table.ZebraColor) : null, grid, cell);
                dc.PushClip(new RectangleGeometry(cell));
                DrawText(dc, "{" + column.Binding + "}", table.FontFamily, table.FontSize, false, false, Res("Brush.TextSecondary"), cell, column.Alignment, VAlign.Middle, false);
                dc.Pop();
            }
            x += w;
        }
    }

    private void DrawSelection(DrawingContext dc, ReportElement element)
    {
        var rect = ViewRect(element);
        var pen = new Pen(Res("Brush.Selection"), 1.5);
        dc.DrawRectangle(null, pen, rect);

        double size = (double)FindResource("Size.Handle");
        foreach (var center in HandleCenters(rect))
            dc.DrawRectangle(Res("Brush.Surface"), pen, new Rect(center.X - size / 2, center.Y - size / 2, size, size));
    }

    // ---------- helpers ----------

    private static IEnumerable<Point> HandleCenters(Rect r)
    {
        // 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L
        yield return r.TopLeft;
        yield return new Point(r.X + r.Width / 2, r.Y);
        yield return r.TopRight;
        yield return new Point(r.Right, r.Y + r.Height / 2);
        yield return r.BottomRight;
        yield return new Point(r.X + r.Width / 2, r.Bottom);
        yield return r.BottomLeft;
        yield return new Point(r.X, r.Y + r.Height / 2);
    }

    private static string FamilyName(string family) => StandardFonts.ResolveFamily(family) switch
    {
        StandardFonts.Family.Courier => "Courier New",
        StandardFonts.Family.Times => "Times New Roman",
        _ => "Arial",
    };

    private Brush Color(string? hex) => Optional(hex) ?? Res("Brush.TextPrimary");

    private Brush? Optional(string? hex)
    {
        if (!RgbColor.TryParse(hex, out var color)) return null;
        if (_brushCache.TryGetValue(hex!, out var cached)) return cached;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return _brushCache[hex!] = brush;
    }

    private Pen? Stroke(string? hex, double widthMm)
    {
        var brush = Optional(hex);
        return brush is null || widthMm <= 0 ? null : new Pen(brush, Math.Max(1, widthMm * Scale));
    }

    private BitmapSource? LoadBitmap(ImageElement element)
    {
        if (string.IsNullOrWhiteSpace(element.EmbeddedImageBase64)) return null;
        var key = element.Id + ":" + element.EmbeddedImageBase64.Length;
        if (_imageCache.TryGetValue(key, out var cached)) return cached;

        BitmapSource? bitmap = null;
        try
        {
            using var stream = new MemoryStream(System.Convert.FromBase64String(element.EmbeddedImageBase64));
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            bitmap = image;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
        {
            // Shown as the "Image" placeholder instead.
        }
        return _imageCache[key] = bitmap;
    }

    // ---------- hit testing ----------

    private Point ToMm(Point view) => new((view.X - Padding) / Scale, (view.Y - Padding) / Scale);

    private ReportElement? ElementAt(Point view)
    {
        if (_vm is null) return null;
        double tolerance = HitTolerancePx;
        for (int i = _vm.Design.Elements.Count - 1; i >= 0; i--)
        {
            var element = _vm.Design.Elements[i];
            var rect = ViewRect(element);
            bool hit = element is LineElement
                ? DistanceToSegment(view, rect.TopLeft, rect.BottomRight) <= tolerance
                : new Rect(rect.X - tolerance, rect.Y - tolerance, rect.Width + 2 * tolerance, rect.Height + 2 * tolerance).Contains(view);
            if (hit) return element;
        }
        return null;
    }

    private int HandleAt(Point view)
    {
        if (_vm?.SelectedElement is not { } selected) return -1;
        double reach = (double)FindResource("Size.Handle") / 2 + HitTolerancePx;
        int index = 0;
        foreach (var center in HandleCenters(ViewRect(selected)))
        {
            if (Math.Abs(view.X - center.X) <= reach && Math.Abs(view.Y - center.Y) <= reach) return index;
            index++;
        }
        return -1;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var d = b - a;
        double lengthSquared = d.X * d.X + d.Y * d.Y;
        double t = lengthSquared == 0 ? 0 : Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / lengthSquared, 0, 1);
        var nearest = new Point(a.X + t * d.X, a.Y + t * d.Y);
        return (p - nearest).Length;
    }

    private static double Snap(double mm) => Math.Round(mm / DesignerViewModel.SnapMm) * DesignerViewModel.SnapMm;

    private static Cursor CursorFor(int handle) => handle switch
    {
        0 or 4 => Cursors.SizeNWSE,
        2 or 6 => Cursors.SizeNESW,
        1 or 5 => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    // ---------- input ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_vm is null) return;
        Focus();

        var view = e.GetPosition(this);
        int handle = HandleAt(view);
        var target = handle >= 0 ? _vm.SelectedElement : ElementAt(view);

        _vm.Select(target);
        if (target is null) return;

        _dragged = target;
        _handle = handle;
        _mode = handle >= 0 ? DragMode.Resize : DragMode.Move;
        _startMm = ToMm(view);
        _startRect = new Rect(target.X, target.Y, target.Width, target.Height);
        _vm.BeginEdit();
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_vm is null) return;

        var view = e.GetPosition(this);
        if (_mode == DragMode.None || _dragged is null)
        {
            int hover = HandleAt(view);
            Cursor = hover >= 0 ? CursorFor(hover) : ElementAt(view) is not null ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        var now = ToMm(view);
        double dx = now.X - _startMm.X, dy = now.Y - _startMm.Y;

        if (_mode == DragMode.Move)
        {
            _vm.ApplyGeometry(_dragged, Snap(_startRect.X + dx), Snap(_startRect.Y + dy), _startRect.Width, _startRect.Height);
            return;
        }

        bool left = _handle is 0 or 6 or 7, right = _handle is 2 or 3 or 4;
        bool top = _handle is 0 or 1 or 2, bottom = _handle is 4 or 5 or 6;
        double minH = _dragged is LineElement ? 0 : MinSizeMm;

        double l = left ? Snap(_startRect.X + dx) : _startRect.X;
        double r = right ? Snap(_startRect.Right + dx) : _startRect.Right;
        double t = top ? Snap(_startRect.Y + dy) : _startRect.Y;
        double b = bottom ? Snap(_startRect.Bottom + dy) : _startRect.Bottom;

        if (r - l < MinSizeMm) { if (left) l = r - MinSizeMm; else r = l + MinSizeMm; }
        if (b - t < minH) { if (top) t = b - minH; else b = t + minH; }

        _vm.ApplyGeometry(_dragged, l, t, r - l, b - t);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_mode == DragMode.None) return;
        _mode = DragMode.None;
        _dragged = null;
        ReleaseMouseCapture();
        _vm?.EndEdit();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null) return;

        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Delete: _vm.DeleteSelected(); break;
            case Key.Escape: _vm.Select(null); break;
            case Key.Left: _vm.Nudge(-step, 0); break;
            case Key.Right: _vm.Nudge(step, 0); break;
            case Key.Up: _vm.Nudge(0, -step); break;
            case Key.Down: _vm.Nudge(0, step); break;
            default: return;
        }
        e.Handled = true;
    }
}
