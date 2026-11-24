using AReport.Barcodes;
using AReport.Model;

namespace AReport.Services;

public interface IDesignValidator
{
    /// <summary>Returns human-readable problems that would prevent rendering (empty when valid).</summary>
    IReadOnlyList<string> Validate(ReportDesign design);
}

public sealed class DesignValidator : IDesignValidator
{
    public IReadOnlyList<string> Validate(ReportDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var issues = new List<string>();
        var page = design.Page;

        if (page.WidthMm <= 0 || page.HeightMm <= 0)
            issues.Add("The page size must be greater than zero.");
        if (page.MarginLeftMm < 0 || page.MarginTopMm < 0 || page.MarginRightMm < 0 || page.MarginBottomMm < 0)
            issues.Add("Page margins cannot be negative.");
        if (page.MarginTopMm + page.MarginBottomMm >= page.HeightMm || page.MarginLeftMm + page.MarginRightMm >= page.WidthMm)
            issues.Add("The page margins leave no printable area.");

        var bands = new (string Name, Band? Band)[]
        {
            ("Report header", design.ReportHeader), ("Page header", design.PageHeader),
            ("Page footer", design.PageFooter), ("Report footer", design.ReportFooter),
        };
        foreach (var (name, band) in bands.Where(b => b.Band is not null))
            if (band!.HeightMm < 0) issues.Add($"The {name.ToLowerInvariant()} has a negative height.");

        double repeating = (design.PageHeader?.HeightMm ?? 0) + (design.PageFooter?.HeightMm ?? 0);
        if (repeating >= page.HeightMm - page.MarginTopMm - page.MarginBottomMm)
            issues.Add("The page header and footer leave no room for the body (no printable area).");

        ValidateDictionary(design.Dictionary, issues);

        var ids = new HashSet<string>();
        var duplicates = new HashSet<string>();
        Walk(design.Elements, "the body", allowFlow: true, ids, duplicates, issues);
        foreach (var (name, band) in bands.Where(b => b.Band is not null))
            Walk(band!.Elements, name.ToLowerInvariant(), allowFlow: false, ids, duplicates, issues);

        foreach (var id in duplicates) issues.Add($"The element id '{id}' is used more than once.");
        return issues;
    }

    private static void ValidateDictionary(ReportDictionary dictionary, List<string> issues)
    {
        foreach (var group in dictionary.Variables.Where(v => !string.IsNullOrWhiteSpace(v.Name)).GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add($"The variable '{group.Key}' is defined more than once.");
        foreach (var group in dictionary.DataSources.Where(s => !string.IsNullOrWhiteSpace(s.Name)).GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            issues.Add($"The data source '{group.Key}' is defined more than once.");
        if (dictionary.Variables.Any(v => string.IsNullOrWhiteSpace(v.Name)) || dictionary.DataSources.Any(s => string.IsNullOrWhiteSpace(s.Name)))
            issues.Add("Every variable and data source needs a name.");
    }

    private void Walk(IEnumerable<ReportElement> elements, string where, bool allowFlow, HashSet<string> ids, HashSet<string> duplicates, List<string> issues)
    {
        foreach (var element in elements)
        {
            string name = element.Label;
            if (!string.IsNullOrEmpty(element.Id) && !ids.Add(element.Id)) duplicates.Add(element.Id);
            if (element.Width < 0 || element.Height < 0) issues.Add($"'{name}' has a negative size.");

            if (!allowFlow && element is TableElement or RepeaterElement)
            {
                issues.Add($"'{name}' is a table or data band, which can only be placed in the body (not in {where}).");
                continue;
            }

            switch (element)
            {
                case TextElement t:
                    if (t.FontSize <= 0) issues.Add($"'{name}' needs a font size greater than zero.");
                    if (t.PaddingMm < 0 || t.BorderWidthMm < 0) issues.Add($"'{name}' has a negative padding or border width.");
                    break;

                case ImageElement i when string.IsNullOrWhiteSpace(i.ImageKey) && string.IsNullOrWhiteSpace(i.EmbeddedImageBase64):
                    issues.Add($"'{name}' needs an image key or embedded image data.");
                    break;

                case BarcodeElement b:
                    if (string.IsNullOrWhiteSpace(b.Content)) issues.Add($"'{name}' needs barcode content.");
                    if (b.Symbology == BarcodeSymbology.QrCode && !Enum.TryParse<QrEccLevel>(b.ErrorCorrection, true, out _))
                        issues.Add($"'{name}' has an unknown error correction level '{b.ErrorCorrection}' (use Low, Medium, Quartile or High).");
                    break;

                case ShapeElement s when s.StrokeWidthMm < 0:
                    issues.Add($"'{name}' has a negative stroke width.");
                    break;

                case TableElement table:
                    if (string.IsNullOrWhiteSpace(table.DataSource))
                        issues.Add($"'{name}' needs a data source.");
                    if (table.Columns.Count == 0)
                        issues.Add($"'{name}' needs at least one column.");
                    if (table.Columns.Any(c => c.Weight <= 0))
                        issues.Add($"'{name}' has a column with a weight of zero or less.");
                    if (table.RowHeightMm <= 0 || table.HeaderHeightMm <= 0)
                        issues.Add($"'{name}' needs row and header heights greater than zero.");
                    if (table.Width <= 0)
                        issues.Add($"'{name}' needs a width greater than zero.");
                    break;

                case RepeaterElement repeater:
                    if (string.IsNullOrWhiteSpace(repeater.DataSource))
                        issues.Add($"'{name}' needs a data source.");
                    if (repeater.Row.HeightMm <= 0)
                        issues.Add($"'{name}' needs a row height greater than zero.");
                    if (repeater.Header.HeightMm < 0 || (repeater.Footer?.HeightMm ?? 0) < 0)
                        issues.Add($"'{name}' has a negative header or footer height.");
                    Walk(repeater.Header.Elements, $"the header of '{name}'", false, ids, duplicates, issues);
                    Walk(repeater.Row.Elements, $"the rows of '{name}'", false, ids, duplicates, issues);
                    Walk(repeater.Footer?.Elements ?? [], $"the footer of '{name}'", false, ids, duplicates, issues);
                    break;
            }
        }
    }
}
