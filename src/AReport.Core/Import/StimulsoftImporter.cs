using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AReport.Imaging;
using AReport.Model;

namespace AReport.Import;

/// <summary>
/// Converts Stimulsoft Reports (<c>.mrt</c>, XML format) to <see cref="ReportDesign"/>. Page, bands, text (fonts, colours, borders,
/// alignment, rotation, Persian digits), images, lines, rectangles, shapes, barcodes, the dictionary and the usual expressions
/// (fields, variables, <c>Sum/Count/Avg/Min/Max</c>, <c>Line</c>, <c>PageNumber</c>) are converted. Everything else is listed in
/// <see cref="ImportResult.Messages"/> instead of being dropped silently.
/// </summary>
public sealed partial class StimulsoftImporter
{
    private const double PixelMm = 25.4 / 96;

    public ImportResult ImportFile(string path)
    {
        var result = Import(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        return result;
    }

    public ImportResult Import(string xml, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(xml);
        XDocument document;
        try
        {
            document = XDocument.Parse(xml.TrimStart('\uFEFF'));
        }
        catch (System.Xml.XmlException ex)
        {
            throw new ReportException("The file is not a valid Stimulsoft report (.mrt): " + ex.Message, ex);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "StiSerializer")
            throw new ReportException("The file is not a Stimulsoft report (.mrt).");

        var state = new State { UnitMm = UnitFactor(Text(root, "ReportUnit")) };
        var design = new ReportDesign
        {
            Name = FirstNonEmpty(Text(root, "ReportName"), Text(root, "ReportAlias"), fallbackName, "Imported report"),
        };

        ImportDictionary(root, design, state);

        var pages = root.Descendants().Where(e => TypeOf(e) == "Page" && e.Attribute("Ref") is not null).ToList();
        if (pages.Count == 0) throw new ReportException("The Stimulsoft report has no page.");
        if (pages.Count > 1) state.Warn($"The report has {pages.Count} pages; only the first page design was imported.");

        ImportPage(pages[0], design, state);
        return new ImportResult(design, state.Messages);
    }

    // ---------- state ----------

    private sealed class State
    {
        public double UnitMm;
        public double MarginLeft, MarginTop;
        public Dictionary<string, string> BandSources = new(StringComparer.OrdinalIgnoreCase);
        public List<ImportMessage> Messages { get; } = [];
        private readonly HashSet<string> _seen = [];

        public void Warn(string text) { if (_seen.Add("W" + text)) Messages.Add(new ImportMessage(ImportSeverity.Warning, text)); }
        public void Info(string text) { if (_seen.Add("I" + text)) Messages.Add(new ImportMessage(ImportSeverity.Info, text)); }
    }

    private static double UnitFactor(string? unit) => unit switch
    {
        "Centimeters" => 10,
        "Millimeters" => 1,
        "HundredthsOfInch" => 0.254,
        _ => 25.4,
    };

    // ---------- dictionary ----------

    private static void ImportDictionary(XElement root, ReportDesign design, State state)
    {
        var dictionary = root.Element("Dictionary");
        if (dictionary is null) return;

        foreach (var source in dictionary.Element("DataSources")?.Elements() ?? [])
        {
            var definition = new DataSourceDefinition { Name = FirstNonEmpty(Text(source, "Name"), source.Name.LocalName) };
            foreach (var column in source.Element("Columns")?.Elements("value") ?? [])
            {
                // "ORIGINAL,Name,NameInSource,Alias,System.Type,key"
                var parts = column.Value.Split(',');
                if (parts.Length < 5 || parts[1].Length == 0) continue;
                definition.Fields.Add(new DataFieldDefinition { Name = Decode(parts[1]), Type = TypeOfColumn(parts[4]) });
            }
            design.Dictionary.DataSources.Add(definition);
        }

        foreach (var variable in dictionary.Element("Variables")?.Elements("value") ?? [])
        {
            var parts = variable.Value.Split(',');
            if (parts.Length < 5 || parts[1].Length == 0) continue;
            string value = parts.Length > 5 ? Decode(parts[5]) : "";
            design.Dictionary.Variables.Add(new VariableDefinition
            {
                Name = Decode(parts[1]),
                Type = TypeOfColumn(parts[4]),
                DefaultValue = value.Length > 0 ? value : null,
                Description = parts[2].Length > 0 && parts[2] != parts[1] ? Decode(parts[2]) : null,
            });
        }

        if (dictionary.Element("Relations")?.Elements().Any() == true)
            state.Warn("Data relations (master-detail) are not supported and were skipped.");
    }

    private static VariableType TypeOfColumn(string clrType) => clrType.Trim() switch
    {
        "System.Decimal" or "System.Double" or "System.Single" or "System.Int16" or "System.Int32" or "System.Int64"
            or "System.Byte" or "System.UInt16" or "System.UInt32" or "System.UInt64" => VariableType.Number,
        "System.DateTime" or "System.DateTimeOffset" => VariableType.Date,
        "System.Boolean" => VariableType.Boolean,
        _ => VariableType.Text,
    };

    // ---------- page ----------

    private void ImportPage(XElement page, ReportDesign design, State state)
    {
        double u = state.UnitMm;
        design.Page.WidthMm = Round(Number(page, "PageWidth", 8.27) * u);
        design.Page.HeightMm = Round(Number(page, "PageHeight", 11.69) * u);

        var margins = Numbers(Text(page, "Margins"));
        if (margins.Length == 4)
        {
            design.Page.MarginLeftMm = Round(margins[0] * u);
            design.Page.MarginTopMm = Round(margins[1] * u);
            design.Page.MarginRightMm = Round(margins[2] * u);
            design.Page.MarginBottomMm = Round(margins[3] * u);
        }
        state.MarginLeft = design.Page.MarginLeftMm;
        state.MarginTop = design.Page.MarginTopMm;

        var components = page.Element("Components")?.Elements().ToList() ?? [];

        // data band name → data source, so Count(DataBand1) can become Count(Source)
        foreach (var band in components.Where(c => TypeOf(c) == "DataBand"))
            state.BandSources[Text(band, "Name") ?? band.Name.LocalName] = Text(band, "DataSourceName") ?? "";

        // Page-level components print on every page; bands are converted in reading order.
        var ordered = components.OrderBy(c => RectOf(c, state).Y).ToList();
        var pendingHeaders = new List<XElement>();
        RepeaterElement? lastRepeater = null;

        foreach (var component in components.Where(c => !IsBand(TypeOf(c))))
            ImportFreeComponent(component, design, state);

        foreach (var component in ordered.Where(c => IsBand(TypeOf(c))))
        {
            switch (TypeOf(component))
            {
                case "PageHeaderBand":
                    design.PageHeader = ImportBand(component, state);
                    break;
                case "PageFooterBand":
                    design.PageFooter = ImportBand(component, state);
                    break;
                case "ReportTitleBand":
                    design.ReportHeader = ImportBand(component, state);
                    break;
                case "ReportSummaryBand":
                    design.ReportFooter = ImportBand(component, state);
                    break;
                case "HeaderBand" or "ColumnHeaderBand":
                    pendingHeaders.Add(component);
                    lastRepeater = null;
                    break;
                case "DataBand":
                    lastRepeater = ImportDataBand(component, pendingHeaders, design, state);
                    pendingHeaders.Clear();
                    break;
                case "FooterBand" or "ColumnFooterBand":
                    if (lastRepeater is null)
                    {
                        state.Warn($"Footer band '{Text(component, "Name")}' has no data band before it and became the report footer.");
                        design.ReportFooter ??= ImportBand(component, state);
                    }
                    else AppendFooter(lastRepeater, component, state);
                    break;
                default:
                    state.Warn($"Band '{Text(component, "Name")}' (type {TypeOf(component)}) is not supported and was skipped.");
                    break;
            }
        }
        if (pendingHeaders.Count > 0)
            state.Warn("A header band without a data band was skipped.");
    }

    private static bool IsBand(string type) =>
        type.EndsWith("Band", StringComparison.Ordinal);

    private Band ImportBand(XElement band, State state)
    {
        var rect = RectOf(band, state);
        var result = new Band { HeightMm = Round(rect.H) };
        foreach (var child in band.Element("Components")?.Elements() ?? [])
            AddComponent(child, rect.X, 0, result.Elements, state, bandRelative: true);
        return result;
    }

    private RepeaterElement ImportDataBand(XElement dataBand, List<XElement> headers, ReportDesign design, State state)
    {
        var rect = RectOf(dataBand, state);
        string source = Text(dataBand, "DataSourceName") ?? "";
        if (source.Length == 0) state.Warn($"Data band '{Text(dataBand, "Name")}' has no data source.");

        var repeater = new RepeaterElement
        {
            Name = Text(dataBand, "Name") ?? "Data band",
            DataSource = source,
            X = Round(state.MarginLeft + rect.X),
            Width = Round(rect.W),
            Row = new Band { HeightMm = Round(rect.H) },
            Header = new Band { HeightMm = 0 },
            RepeatHeaderOnEveryPage = false,
        };

        double top = rect.Y;
        if (headers.Count > 0)
        {
            var first = RectOf(headers[0], state);
            var last = RectOf(headers[^1], state);
            top = first.Y;
            repeater.Header.HeightMm = Round(last.Y + last.H - first.Y);
            foreach (var header in headers)
            {
                var r = RectOf(header, state);
                foreach (var child in header.Element("Components")?.Elements() ?? [])
                    AddComponent(child, r.X - rect.X, r.Y - first.Y, repeater.Header.Elements, state, bandRelative: true);
                if (string.Equals(Text(header, "PrintOnAllPages"), "True", StringComparison.OrdinalIgnoreCase))
                    repeater.RepeatHeaderOnEveryPage = true;
            }
        }
        repeater.Y = Round(state.MarginTop + top);
        repeater.Height = Round(repeater.Header.HeightMm + rect.H * 2);

        foreach (var child in dataBand.Element("Components")?.Elements() ?? [])
            AddComponent(child, 0, 0, repeater.Row.Elements, state, bandRelative: true);

        if (!string.IsNullOrEmpty(Text(dataBand, "Sort")) && Text(dataBand, "Sort") != "")
            state.Warn($"Sorting of data band '{repeater.Name}' is not imported; sort the data before passing it in.");
        if (!string.IsNullOrEmpty(Text(dataBand, "MasterComponent")))
            state.Warn($"Data band '{repeater.Name}' is a detail of another band (master-detail), which is not supported.");

        design.Elements.Add(repeater);
        return repeater;
    }

    private void AppendFooter(RepeaterElement repeater, XElement footer, State state)
    {
        var rect = RectOf(footer, state);
        repeater.Footer ??= new Band { HeightMm = 0 };
        double offset = repeater.Footer.HeightMm;
        foreach (var child in footer.Element("Components")?.Elements() ?? [])
            AddComponent(child, 0, offset, repeater.Footer.Elements, state, bandRelative: true);
        repeater.Footer.HeightMm = Round(offset + rect.H);
    }

    private void ImportFreeComponent(XElement component, ReportDesign design, State state)
    {
        var elements = new List<ReportElement>();
        AddComponent(component, state.MarginLeft, state.MarginTop, elements, state, bandRelative: false);
        foreach (var element in elements)
        {
            element.RepeatOnEveryPage = true;   // components placed directly on a Stimulsoft page print on every page
            design.Elements.Add(element);
        }
    }

    // ---------- components ----------

    private void AddComponent(XElement c, double offsetX, double offsetY, List<ReportElement> output, State state, bool bandRelative)
    {
        string type = TypeOf(c);
        string name = Text(c, "Name") ?? c.Name.LocalName;
        var rect = RectOf(c, state);
        double x = Round(offsetX + rect.X), y = Round(offsetY + rect.Y);

        switch (type)
        {
            case "Text":
                output.Add(ImportText(c, name, x, y, rect, state));
                break;

            case "Image":
                if (ImportImage(c, name, x, y, rect, state) is { } image) output.Add(image);
                break;

            case "HorizontalLinePrimitive":
                output.Add(new LineElement { Name = name, X = x, Y = y, Width = Round(rect.W), Height = 0, StrokeColor = ColorHex(Text(c, "Color")) ?? "#000000", StrokeWidthMm = LineWidth(c) });
                break;

            case "VerticalLinePrimitive":
                output.Add(new LineElement { Name = name, X = x, Y = y, Width = 0, Height = Round(rect.H), StrokeColor = ColorHex(Text(c, "Color")) ?? "#000000", StrokeWidthMm = LineWidth(c) });
                break;

            case "RectanglePrimitive":
                output.Add(new ShapeElement
                {
                    Name = name, X = x, Y = y, Width = Round(rect.W), Height = Round(rect.H), Shape = ShapeKind.Rectangle,
                    FillColor = null, StrokeColor = ColorHex(Text(c, "Color")) ?? "#000000", StrokeWidthMm = LineWidth(c),
                });
                break;

            case "RoundedRectanglePrimitive":
            {
                double round = Number(c, "Round", 0.2);
                output.Add(new ShapeElement
                {
                    Name = name, X = x, Y = y, Width = Round(rect.W), Height = Round(rect.H), Shape = ShapeKind.RoundedRectangle,
                    FillColor = null, StrokeColor = ColorHex(Text(c, "Color")) ?? "#000000", StrokeWidthMm = LineWidth(c),
                    CornerRadiusMm = Round(Math.Min(Math.Max(0.1, round) * Math.Min(rect.W, rect.H), Math.Min(rect.W, rect.H) / 2)),
                });
                break;
            }

            case "Shape":
                output.Add(ImportShape(c, name, x, y, rect, state));
                break;

            case "BarCode":
                if (ImportBarcode(c, name, x, y, rect, state) is { } barcode) output.Add(barcode);
                break;

            case "Panel" or "Container" or "GroupBox" or "Clone":
                foreach (var child in c.Element("Components")?.Elements() ?? [])
                    AddComponent(child, x, y, output, state, bandRelative);
                break;

            case "StartPointPrimitive" or "EndPointPrimitive" or "Cap" or "ReportTitleBand":
                break; // anchors of linked primitives: nothing to draw

            default:
                state.Warn($"Component '{name}' (type {type}) is not supported and was skipped.");
                break;
        }
    }

    private TextElement ImportText(XElement c, string name, double x, double y, (double X, double Y, double W, double H) rect, State state)
    {
        var text = new TextElement { Name = name, X = x, Y = y, Width = Round(rect.W), Height = Round(rect.H) };

        string expression = Text(c, "Text") ?? "";
        string? format = TextFormatOf(c, state);
        text.Text = ConvertExpressions(expression, state, format);

        ApplyFont(text, Text(c, "Font"));
        text.Color = ColorHex(Text(c, "TextBrush")) ?? "#000000";
        text.FillColor = ColorHex(Text(c, "Brush"));
        text.HorizontalAlignment = Text(c, "HorAlignment") switch { "Center" => HAlign.Center, "Right" => HAlign.Right, _ => HAlign.Left };
        text.VerticalAlignment = Text(c, "VertAlignment") switch { "Center" => VAlign.Middle, "Bottom" => VAlign.Bottom, _ => VAlign.Top };
        text.CanGrow = IsTrue(Text(c, "CanGrow"));
        text.GrowToHeight = IsTrue(Text(c, "GrowToHeight"));

        var margins = Numbers(Text(c, "Margins"));
        if (margins.Length == 4) text.PaddingMm = Round((margins[0] + margins[1] + margins[2] + margins[3]) / 4 * 0.254);
        else text.PaddingMm = 0;

        ApplyBorder(text, Text(c, "Border"));

        text.WordWrap = text.CanGrow;
        foreach (var option in (Text(c, "TextOptions") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = option.Split('=', 2);
            if (pair.Length != 2) continue;
            switch (pair[0].Trim())
            {
                case "Angle" when double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var angle): text.Rotation = angle; break;
                case "WordWrap": text.WordWrap = IsTrue(pair[1]); break;
                case "RightToLeft" when IsTrue(pair[1]): text.Direction = TextDirection.RightToLeft; break;
            }
        }

        if (!string.IsNullOrEmpty(Text(c, "Conditions")))
            state.Warn($"Text '{name}' has conditional formatting, which is not imported.");
        return text;
    }

    private static void ApplyFont(TextElement text, string? font)
    {
        if (string.IsNullOrWhiteSpace(font)) return;
        var parts = font.Split(',').Select(p => p.Trim()).ToList();

        string family = parts[0];
        // "IRANSans(FaNum)" is the font variant that draws Persian digits for 0-9.
        var variant = FontVariantPattern().Match(family);
        if (variant.Success)
        {
            family = variant.Groups[1].Value.Trim();
            if (variant.Groups[2].Value.Contains("FaNum", StringComparison.OrdinalIgnoreCase)) text.Digits = DigitStyle.Persian;
        }
        text.FontFamily = family;

        if (parts.Count > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var size) && size > 0)
            text.FontSize = size;
        foreach (var style in parts.Skip(2).SelectMany(p => p.Split(['!', ' '], StringSplitOptions.RemoveEmptyEntries)))
        {
            if (style.Equals("Bold", StringComparison.OrdinalIgnoreCase)) text.Bold = true;
            if (style.Equals("Italic", StringComparison.OrdinalIgnoreCase)) text.Italic = true;
        }
    }

    private static void ApplyBorder(TextElement text, string? border)
    {
        if (string.IsNullOrWhiteSpace(border)) return;
        var parts = border.Split(';');
        if (parts.Length < 3) return;

        var sides = BorderSides.None;
        foreach (var side in parts[0].Split(',', StringSplitOptions.TrimEntries))
        {
            sides |= side switch
            {
                "All" => BorderSides.All,
                "Left" => BorderSides.Left,
                "Top" => BorderSides.Top,
                "Right" => BorderSides.Right,
                "Bottom" => BorderSides.Bottom,
                _ => BorderSides.None,
            };
        }
        text.Borders = sides;
        text.BorderColor = ColorHex(parts[1]) ?? "#000000";
        if (double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
            text.BorderWidthMm = Round(Math.Max(0.1, width * PixelMm));
    }

    private static string? TextFormatOf(XElement c, State state)
    {
        var format = c.Element("TextFormat");
        if (format is null) return null;
        string kind = TypeOf(format);
        if (kind is "NumberFormat" or "NumberFormatService")
        {
            int digits = (int)Number(format, "DecimalDigits", 2);
            return "N" + digits;
        }
        if (kind is "PercentageFormat" or "PercentageFormatService") return "P" + (int)Number(format, "DecimalDigits", 2);
        if (kind is "CurrencyFormat" or "CurrencyFormatService") return "C" + (int)Number(format, "DecimalDigits", 2);
        if (kind is not ("GeneralFormat" or "GeneralFormatService" or ""))
            state.Info($"Text format '{kind}' is not converted; the value is shown with the default format.");
        return null;
    }

    private ImageElement? ImportImage(XElement c, string name, double x, double y, (double X, double Y, double W, double H) rect, State state)
    {
        var bytesElement = c.Element("ImageBytes");
        if (bytesElement is null || string.IsNullOrWhiteSpace(bytesElement.Value))
        {
            if (!string.IsNullOrWhiteSpace(Text(c, "DataColumn")))
                state.Warn($"Image '{name}' is bound to the data column '{Text(c, "DataColumn")}'; add an image with that key at run time (ImageKey).");
            else state.Warn($"Image '{name}' has no picture data and was skipped.");
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(bytesElement.Value.Trim());
            if (!ImageReader.TryRead(bytes, out _, out var error))
            {
                state.Warn($"Image '{name}' could not be imported: {error}");
                return null;
            }
            return new ImageElement
            {
                Name = name, X = x, Y = y, Width = Round(rect.W), Height = Round(rect.H),
                EmbeddedImageBase64 = Convert.ToBase64String(bytes),
                Fit = IsTrue(Text(c, "AspectRatio")) ? ImageFit.Contain : ImageFit.Stretch,
            };
        }
        catch (FormatException)
        {
            state.Warn($"Image '{name}' has invalid picture data and was skipped.");
            return null;
        }
    }

    private ShapeElement ImportShape(XElement c, string name, double x, double y, (double X, double Y, double W, double H) rect, State state)
    {
        string kind = c.Element("ShapeType") is { } shapeType ? TypeOf(shapeType) : "";
        var shape = kind switch
        {
            "OvalShapeType" => ShapeKind.Ellipse,
            "TriangleShapeType" => ShapeKind.Triangle,
            "DiamondShapeType" => ShapeKind.Diamond,
            "RoundedRectangleShapeType" => ShapeKind.RoundedRectangle,
            "ArrowShapeType" => ShapeKind.ArrowRight,
            _ => ShapeKind.Rectangle,
        };
        if (kind.Length > 0 && shape == ShapeKind.Rectangle && kind != "RectangleShapeType")
            state.Warn($"Shape '{name}' of type {kind} was imported as a rectangle.");

        return new ShapeElement
        {
            Name = name, X = x, Y = y, Width = Round(rect.W), Height = Round(rect.H), Shape = shape,
            FillColor = ColorHex(Text(c, "Brush")), StrokeColor = ColorHex(Text(c, "BorderColor")) ?? "#000000", StrokeWidthMm = LineWidth(c),
        };
    }

    private BarcodeElement? ImportBarcode(XElement c, string name, double x, double y, (double X, double Y, double W, double H) rect, State state)
    {
        string kind = c.Element("BarCodeType") is { } t ? TypeOf(t) : "";
        var symbology = kind switch
        {
            var k when k.Contains("QRCode", StringComparison.OrdinalIgnoreCase) => BarcodeSymbology.QrCode,
            var k when k.Contains("DataMatrix", StringComparison.OrdinalIgnoreCase) => BarcodeSymbology.DataMatrix,
            var k when k.Contains("Code128", StringComparison.OrdinalIgnoreCase) => BarcodeSymbology.Code128,
            _ => BarcodeSymbology.Code128,
        };
        if (!kind.Contains("QRCode", StringComparison.OrdinalIgnoreCase) && !kind.Contains("DataMatrix", StringComparison.OrdinalIgnoreCase)
            && !kind.Contains("Code128", StringComparison.OrdinalIgnoreCase))
            state.Warn($"Barcode '{name}' of type {kind} was imported as Code 128; check that it still scans.");

        return new BarcodeElement
        {
            Name = name, X = x, Y = y, Width = Round(rect.W), Height = Round(rect.H),
            Symbology = symbology,
            Content = ConvertExpressions(Text(c, "Code") ?? "", state, null),
            ShowText = !string.Equals(Text(c, "ShowLabelText"), "False", StringComparison.OrdinalIgnoreCase),
            ForeColor = ColorHex(Text(c, "ForeColor")) ?? "#000000",
            BackColor = ColorHex(Text(c, "BackColor")),
        };
    }

    private static double LineWidth(XElement c)
    {
        double size = Number(c, "Size", 1);
        return Round(Math.Max(0.1, size * PixelMm));
    }

    // ---------- expressions ----------

    /// <summary>Converts the <c>{...}</c> expressions of a Stimulsoft text into A-Report placeholders.</summary>
    private string ConvertExpressions(string text, State state, string? defaultFormat)
    {
        var sb = new System.Text.StringBuilder();
        int last = 0;
        var matches = ExpressionPattern().Matches(text);
        bool single = matches.Count == 1 && matches[0].Value.Length == text.Trim().Length;

        foreach (Match match in matches)
        {
            sb.Append(EscapeLiteral(text[last..match.Index]));
            string expression = match.Groups[1].Value.Trim();
            string? converted = ConvertExpression(expression, state);
            if (converted is null)
            {
                state.Warn($"The expression {{{expression}}} cannot be converted automatically and is shown as text; replace it with a field, variable or aggregate.");
                sb.Append("{{").Append(expression).Append("}}");
            }
            else
            {
                if (single && defaultFormat is not null && !converted.Contains(':')) converted += ":" + defaultFormat;
                sb.Append('{').Append(converted).Append('}');
            }
            last = match.Index + match.Length;
        }
        sb.Append(EscapeLiteral(text[last..]));
        return sb.ToString();
    }

    private static string EscapeLiteral(string text) => text.Replace("{", "{{").Replace("}", "}}");

    private string? ConvertExpression(string expression, State state)
    {
        switch (expression)
        {
            case "PageNumber": return "PageNumber";
            case "TotalPageCount" or "PageCount": return "PageCount";
            case "Line": return "Line";
            case "DateString" or "Date" or "Today": return "Now:d";
            case "DateTimeString" or "Now" or "DateTime.Now": return "Now:g";
            case "TimeString" or "Time": return "Now:t";
            case "PageNofM": return "PageNumber}/{PageCount";
        }

        var aggregate = AggregatePattern().Match(expression);
        if (aggregate.Success)
        {
            string function = aggregate.Groups[1].Value;
            var arguments = aggregate.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries).Where(a => a.Length > 0).ToList();
            // A lone band name stands for that band's data source.
            for (int i = 0; i < arguments.Count; i++)
                if (state.BandSources.TryGetValue(arguments[i], out var source) && source.Length > 0 && !arguments[i].Contains('.') && arguments.Count == 1)
                    arguments[i] = source;
            return $"{function}({string.Join(", ", arguments)})";
        }

        // fields and variables: letters (any script), digits, underscores, dots
        if (PathPattern().IsMatch(expression)) return Decode(expression);
        return null;
    }

    // ---------- helpers ----------

    private static string TypeOf(XElement e)
    {
        string type = e.Attribute("type")?.Value ?? "";
        int dot = type.LastIndexOf('.');
        if (dot >= 0) type = type[(dot + 1)..];
        if (type.Length > 3 && type.StartsWith("Sti", StringComparison.Ordinal) && char.IsUpper(type[3])) type = type[3..];
        return type;
    }

    private static string? Text(XElement e, string child)
    {
        var element = e.Element(child);
        return element is null ? null : element.Value;
    }

    private static double Number(XElement e, string child, double fallback) =>
        double.TryParse(Text(e, child), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static double[] Numbers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var parts = text.Split(',');
        var result = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out result[i])) return [];
        return result;
    }

    private static (double X, double Y, double W, double H) RectOf(XElement e, State state)
    {
        var n = Numbers(Text(e, "ClientRectangle"));
        if (n.Length != 4) return (0, 0, 0, 0);

        double x = n[0], y = n[1], w = n[2], h = n[3];
        // Primitives that were drawn from their end point back to the start have a negative size.
        if (w < 0) { x += w; w = -w; }
        if (h < 0) { y += h; h = -h; }
        return (x * state.UnitMm, y * state.UnitMm, w * state.UnitMm, h * state.UnitMm);
    }

    private static bool IsTrue(string? value) => string.Equals(value?.Trim(), "True", StringComparison.OrdinalIgnoreCase);

    private static double Round(double value) => Math.Round(value, 2);

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    /// <summary>Stimulsoft escapes characters in names as <c>_x0020_</c>.</summary>
    private static string Decode(string value) =>
        EscapePattern().Replace(value, m => ((char)int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());

    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Black"] = "#000000", ["White"] = "#FFFFFF", ["Red"] = "#FF0000", ["Green"] = "#008000", ["Blue"] = "#0000FF",
        ["Yellow"] = "#FFFF00", ["Gray"] = "#808080", ["Grey"] = "#808080", ["LightGray"] = "#D3D3D3", ["DarkGray"] = "#A9A9A9",
        ["Orange"] = "#FFA500", ["Silver"] = "#C0C0C0", ["Navy"] = "#000080", ["Maroon"] = "#800000", ["Purple"] = "#800080",
    };

    /// <summary>Converts a Stimulsoft colour/brush ("Black", "[r:g:b]", "[a:r:g:b]", "r,g,b") to #RRGGBB; null for transparent/none.</summary>
    internal static string? ColorHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value is "Transparent" or "EmptyBrush" or "Empty" or "None") return null;
        if (value.StartsWith("solid:", StringComparison.OrdinalIgnoreCase)) value = value[6..];
        if (NamedColors.TryGetValue(value, out var named)) return named;
        if (value.StartsWith('#') && value.Length is 7 or 9) return "#" + value[^6..].ToUpperInvariant();

        var parts = value.Trim('[', ']').Split([':', ','], StringSplitOptions.TrimEntries);
        if (parts.Length is 3 or 4 && parts.All(p => byte.TryParse(p, out _)))
        {
            var rgb = parts.Length == 4 ? parts[1..] : parts;
            if (parts.Length == 4 && byte.Parse(parts[0]) == 0) return null; // fully transparent
            return $"#{byte.Parse(rgb[0]):X2}{byte.Parse(rgb[1]):X2}{byte.Parse(rgb[2]):X2}";
        }
        return null;
    }

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex ExpressionPattern();

    [GeneratedRegex(@"^(Sum|Avg|Min|Max|Count)\s*\((.*)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex AggregatePattern();

    [GeneratedRegex(@"^[\p{L}\p{N}_][\p{L}\p{N}_\.]*$")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"_x([0-9A-Fa-f]{4})_")]
    private static partial Regex EscapePattern();

    [GeneratedRegex(@"^(.*?)\((.*)\)$")]
    private static partial Regex FontVariantPattern();
}
