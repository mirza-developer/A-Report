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

        foreach (var duplicate in design.Elements.Where(e => !string.IsNullOrEmpty(e.Id)).GroupBy(e => e.Id).Where(g => g.Count() > 1))
            issues.Add($"The element id '{duplicate.Key}' is used more than once.");

        foreach (var element in design.Elements)
        {
            string name = element.Label;
            if (element.Width < 0 || element.Height < 0)
                issues.Add($"'{name}' has a negative size.");

            switch (element)
            {
                case TextElement t when t.FontSize <= 0:
                    issues.Add($"'{name}' needs a font size greater than zero.");
                    break;
                case ImageElement i when string.IsNullOrWhiteSpace(i.ImageKey) && string.IsNullOrWhiteSpace(i.EmbeddedImageBase64):
                    issues.Add($"'{name}' needs an image key or embedded image data.");
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
            }
        }
        return issues;
    }
}
