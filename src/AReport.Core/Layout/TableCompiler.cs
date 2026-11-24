using AReport.Model;

namespace AReport.Layout;

/// <summary>A <see cref="TableElement"/> is shorthand for a repeater of bordered text cells; the layout engine only knows repeaters.</summary>
public static class TableCompiler
{
    public static RepeaterElement Compile(TableElement table)
    {
        double totalWeight = table.Columns.Sum(c => c.Weight);
        double Width(TableColumn c) => totalWeight > 0 ? table.Width * c.Weight / totalWeight : 0;

        TextElement Cell(double x, double w, double h, string text, TableColumn column, bool header, bool footer) => new()
        {
            Id = "",
            X = x,
            Y = 0,
            Width = w,
            Height = h,
            Text = text,
            FontFamily = table.FontFamily,
            FontSize = table.FontSize,
            Bold = header || footer,
            Color = header ? table.HeaderTextColor : table.TextColor,
            FillColor = header ? table.HeaderFillColor : null,
            Borders = BorderSides.All,
            BorderColor = table.GridColor,
            BorderWidthMm = 0.2,
            HorizontalAlignment = column.Alignment,
            VerticalAlignment = VAlign.Middle,
            WordWrap = false,
            PaddingMm = 1.5,
            Name = column.Header,
        };

        var header = new Band { HeightMm = table.HeaderHeightMm };
        var row = new Band { HeightMm = table.RowHeightMm };
        var footer = table.Columns.Any(c => !string.IsNullOrEmpty(c.Footer)) ? new Band { HeightMm = table.RowHeightMm } : null;

        double x = 0;
        foreach (var column in table.Columns)
        {
            double w = Width(column);
            string headerText = column.Header.Replace("{", "{{").Replace("}", "}}");
            string format = string.IsNullOrEmpty(column.Format) ? "" : ":" + column.Format;
            string cellText = column.Binding == "#" ? "{Line" + format + "}" : "{." + column.Binding + format + "}";

            header.Elements.Add(Cell(x, w, table.HeaderHeightMm, headerText, column, header: true, footer: false));
            row.Elements.Add(Cell(x, w, table.RowHeightMm, cellText, column, header: false, footer: false));
            footer?.Elements.Add(Cell(x, w, table.RowHeightMm, column.Footer ?? "", column, header: false, footer: true));
            x += w;
        }

        return new RepeaterElement
        {
            Id = table.Id,
            Name = table.Name,
            X = table.X,
            Y = table.Y,
            Width = table.Width,
            Height = table.Height,
            DataSource = table.DataSource,
            Header = header,
            Row = row,
            Footer = footer,
            RepeatHeaderOnEveryPage = true,
            ZebraColor = table.ZebraColor,
        };
    }
}
