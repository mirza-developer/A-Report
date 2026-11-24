using System.Text.Json.Serialization;

namespace AReport.Model;

/// <summary>Base of everything that can be placed on a page. Lengths are millimetres from the page's top-left corner.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(RectangleElement), "rectangle")]
[JsonDerivedType(typeof(LineElement), "line")]
[JsonDerivedType(typeof(ImageElement), "image")]
[JsonDerivedType(typeof(TableElement), "table")]
[JsonDerivedType(typeof(ShapeElement), "shape")]
[JsonDerivedType(typeof(BarcodeElement), "barcode")]
[JsonDerivedType(typeof(RepeaterElement), "repeater")]
public abstract class ReportElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 40;
    public double Height { get; set; } = 10;

    /// <summary>Draw on every page (headers, footers, watermarks) instead of only the first one.</summary>
    public bool RepeatOnEveryPage { get; set; }

    /// <summary>Inside a repeater row: stretch to the row's height when the row grows (borders and fills stay aligned).</summary>
    public bool GrowToHeight { get; set; }

    [JsonIgnore]
    public abstract string Kind { get; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Name) ? $"{Kind} {Id[..Math.Min(6, Id.Length)]}" : Name;
}

public sealed class TextElement : ReportElement
{
    public TextElement() { Width = 60; Height = 8; }

    [JsonIgnore]
    public override string Kind => "Text";

    /// <summary>Template: plain text with <c>{Placeholders}</c>.</summary>
    public string Text { get; set; } = "Text";
    public string FontFamily { get; set; } = "Helvetica";
    public double FontSize { get; set; } = 10;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public string Color { get; set; } = "#000000";
    public HAlign HorizontalAlignment { get; set; } = HAlign.Left;
    public VAlign VerticalAlignment { get; set; } = VAlign.Top;
    public bool WordWrap { get; set; } = true;

    /// <summary>Paragraph direction. Auto follows the first strong character (Persian/Arabic → right-to-left).</summary>
    public TextDirection Direction { get; set; } = TextDirection.Auto;

    /// <summary>Draw the digits 0-9 as Persian (۰۱۲۳) or Arabic-Indic (٠١٢٣) digits.</summary>
    public DigitStyle Digits { get; set; } = DigitStyle.Latin;

    /// <summary>Background colour, empty for none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FillColor { get; set; }

    public BorderSides Borders { get; set; } = BorderSides.None;
    public string BorderColor { get; set; } = "#000000";
    public double BorderWidthMm { get; set; } = 0.2;

    /// <summary>Space between the border and the text, in millimetres.</summary>
    public double PaddingMm { get; set; } = 1;

    /// <summary>Counter-clockwise rotation in degrees (90 reads bottom to top).</summary>
    public double Rotation { get; set; }

    /// <summary>In a repeater: the row gets taller when the text needs more lines.</summary>
    public bool CanGrow { get; set; }
}

public sealed class RectangleElement : ReportElement
{
    public RectangleElement() { Width = 40; Height = 20; }

    [JsonIgnore]
    public override string Kind => "Rectangle";

    /// <summary>Hex colour or empty for no fill.</summary>
    public string? FillColor { get; set; }

    /// <summary>Hex colour or empty for no border.</summary>
    public string? StrokeColor { get; set; } = "#000000";
    public double StrokeWidthMm { get; set; } = 0.3;
}

/// <summary>A straight line from (X, Y) to (X + Width, Y + Height).</summary>
public sealed class LineElement : ReportElement
{
    public LineElement() { Width = 60; Height = 0; }

    [JsonIgnore]
    public override string Kind => "Line";

    public string StrokeColor { get; set; } = "#000000";
    public double StrokeWidthMm { get; set; } = 0.3;
}

public sealed class ImageElement : ReportElement
{
    public ImageElement() { Width = 40; Height = 25; }

    [JsonIgnore]
    public override string Kind => "Image";

    /// <summary>Key of an image supplied at run time (<c>ReportData.AddImage</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageKey { get; set; }

    /// <summary>PNG/JPEG stored inside the design itself, base64 encoded. Used when no run-time image is supplied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EmbeddedImageBase64 { get; set; }
    public ImageFit Fit { get; set; } = ImageFit.Contain;
}

/// <summary>Repeats one row per item of a data source and continues on following pages. Height is only the designer preview extent.</summary>
public sealed class TableElement : ReportElement
{
    public TableElement() { Width = 190; Height = 32; }

    [JsonIgnore]
    public override string Kind => "Table";

    public string DataSource { get; set; } = "";
    public List<TableColumn> Columns { get; set; } = [];
    public double HeaderHeightMm { get; set; } = 8;
    public double RowHeightMm { get; set; } = 7;
    public string FontFamily { get; set; } = "Helvetica";
    public double FontSize { get; set; } = 9;
    public string HeaderFillColor { get; set; } = "#E5E7EB";
    public string HeaderTextColor { get; set; } = "#111827";
    public string TextColor { get; set; } = "#000000";
    public string GridColor { get; set; } = "#9CA3AF";

    /// <summary>Optional fill of every second row.</summary>
    public string? ZebraColor { get; set; }
}

public sealed class TableColumn
{
    public string Header { get; set; } = "";

    /// <summary>Field path inside a row (<c>Price</c>, <c>Product.Name</c>) or <c>#</c> for the row number.</summary>
    public string Binding { get; set; } = "";

    /// <summary>Relative width: a column with weight 2 is twice as wide as one with weight 1.</summary>
    public double Weight { get; set; } = 1;
    public HAlign Alignment { get; set; } = HAlign.Left;

    /// <summary>.NET format string such as <c>N2</c> or <c>yyyy-MM-dd</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; set; }

    /// <summary>Template for the totals row, for example <c>{Sum(Items.Amount):N2}</c>. When no column has one there is no totals row.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Footer { get; set; }
}

/// <summary>A geometric shape: rectangle, rounded rectangle, ellipse, triangle, diamond, hexagon, star, arrows…</summary>
public sealed class ShapeElement : ReportElement
{
    public ShapeElement() { Width = 30; Height = 20; }

    [JsonIgnore]
    public override string Kind => "Shape";

    public ShapeKind Shape { get; set; } = ShapeKind.Rectangle;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FillColor { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StrokeColor { get; set; } = "#000000";
    public double StrokeWidthMm { get; set; } = 0.3;

    /// <summary>Corner radius of a rounded rectangle.</summary>
    public double CornerRadiusMm { get; set; } = 3;
}

/// <summary>A QR code, Data Matrix or Code 128 barcode whose content is a template (<c>{InvoiceNumber}</c>).</summary>
public sealed class BarcodeElement : ReportElement
{
    public BarcodeElement() { Width = 30; Height = 30; }

    [JsonIgnore]
    public override string Kind => "Barcode";

    public BarcodeSymbology Symbology { get; set; } = BarcodeSymbology.QrCode;

    /// <summary>The text to encode; may contain placeholders.</summary>
    public string Content { get; set; } = "{Code}";

    /// <summary>QR error correction: Low, Medium, Quartile or High.</summary>
    public string ErrorCorrection { get; set; } = "Medium";

    /// <summary>Code 128 only: print the text under the bars.</summary>
    public bool ShowText { get; set; } = true;

    public string ForeColor { get; set; } = "#000000";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackColor { get; set; }

    /// <summary>Empty margin around the symbol, in modules (scanners need one).</summary>
    public double QuietZoneModules { get; set; } = 1;
}

/// <summary>
/// A data band: prints <see cref="Header"/> once, <see cref="Row"/> for every item of the data source, and <see cref="Footer"/> once
/// at the end (totals). Flows over pages; the header repeats on each page. Element coordinates inside the sections are relative to
/// the repeater's left edge and the section's top. The repeater's own Height is only the designer preview.
/// </summary>
public sealed class RepeaterElement : ReportElement
{
    public RepeaterElement() { Width = 190; Height = 30; }

    [JsonIgnore]
    public override string Kind => "Data band";

    public string DataSource { get; set; } = "";

    public Band Header { get; set; } = new() { HeightMm = 8 };

    public Band Row { get; set; } = new() { HeightMm = 7 };

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Band? Footer { get; set; }

    public bool RepeatHeaderOnEveryPage { get; set; } = true;

    /// <summary>Background of every second row for text cells without their own fill.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ZebraColor { get; set; }
}
