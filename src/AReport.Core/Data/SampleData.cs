using AReport.Model;
using AReport.Templates;

namespace AReport.Data;

/// <summary>Invents placeholder data for a design, so the designer can preview it without a real data source.</summary>
public static class SampleData
{
    private const int SampleRows = 12;

    public static ReportData CreateFor(ReportDesign design, ITemplateEngine? templates = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        templates ??= new TemplateEngine(new ValueResolver());

        var data = new ReportData();
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var text in design.Elements.OfType<TextElement>())
        {
            foreach (var path in templates.ExtractPlaceholders(text.Text))
                AddPath(variables, path);
        }

        foreach (var table in design.Elements.OfType<TableElement>().Where(t => !string.IsNullOrWhiteSpace(t.DataSource)))
        {
            var rows = new List<object?>();
            for (int i = 1; i <= SampleRows; i++)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var column in table.Columns.Where(c => c.Binding is { Length: > 0 } && c.Binding != "#"))
                    SetPath(row, column.Binding, $"{column.Binding} {i}");
                rows.Add(row);
            }
            data.AddDataSource(table.DataSource, rows);
        }

        foreach (var pair in variables)
            data.AddVariable(pair.Key, pair.Value);
        return data;
    }

    private static void AddPath(Dictionary<string, object?> root, string path)
    {
        var head = path.Split('.')[0];
        if (TemplateEngine.BuiltIns.Contains(head, StringComparer.OrdinalIgnoreCase)) return;
        SetPath(root, path, $"‹{path}›");
    }

    private static void SetPath(Dictionary<string, object?> root, string path, object value)
    {
        var segments = path.Split('.');
        var current = root;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current.TryGetValue(segments[i], out var existing) && existing is Dictionary<string, object?> child)
            {
                current = child;
                continue;
            }
            child = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            current[segments[i]] = child;
            current = child;
        }
        current[segments[^1]] = value;
    }
}
