using System.Globalization;
using AReport.Model;
using AReport.Templates;

namespace AReport.Data;

/// <summary>
/// Invents placeholder data for a design so it can be previewed without the real application data: a value for every variable,
/// rows for every data source (typed from the dictionary), and a placeholder picture for every image key.
/// </summary>
public static class SampleData
{
    private const int SampleRows = 12;
    private const string PlaceholderPng = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAIAAABLbSncAAAAGklEQVR42mM4ceIEw7Nnzxhw0fjkQPQQ1w8ApbChQZ0dXE0AAAAASUVORK5CYII=";

    public static ReportData CreateFor(ReportDesign design, ITemplateEngine? templates = null)
    {
        ArgumentNullException.ThrowIfNull(design);
        templates ??= new TemplateEngine(new ValueResolver());

        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, SourceSample>(StringComparer.OrdinalIgnoreCase);
        var imageKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declared = design.Dictionary.Variables.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var source in design.Dictionary.DataSources.Where(s => !string.IsNullOrWhiteSpace(s.Name)))
        {
            var sample = Source(sources, source.Name);
            foreach (var field in source.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Name))) sample.Fields[field.Name] = field.Type;
        }
        foreach (var image in design.Dictionary.Images) imageKeys.Add(image.Name);

        void Visit(IEnumerable<ReportElement> elements, string? rowSource)
        {
            foreach (var element in elements)
            {
                switch (element)
                {
                    case TextElement text: Scan(text.Text, rowSource); break;
                    case BarcodeElement barcode: Scan(barcode.Content, rowSource); break;
                    case ImageElement image when !string.IsNullOrWhiteSpace(image.ImageKey) && string.IsNullOrWhiteSpace(image.EmbeddedImageBase64):
                        imageKeys.Add(image.ImageKey);
                        break;
                    case TableElement table:
                        var tableSource = Source(sources, table.DataSource);
                        foreach (var column in table.Columns.Where(c => c.Binding is { Length: > 0 } && c.Binding != "#"))
                            tableSource.Fields.TryAdd(column.Binding, VariableType.Text);
                        foreach (var column in table.Columns) Scan(column.Footer, table.DataSource);
                        break;
                    case RepeaterElement repeater:
                        Source(sources, repeater.DataSource);
                        Visit(repeater.Header.Elements, repeater.DataSource);
                        Visit(repeater.Row.Elements, repeater.DataSource);
                        Visit(repeater.Footer?.Elements ?? [], repeater.DataSource);
                        break;
                }
            }
        }

        void Scan(string? template, string? rowSource)
        {
            foreach (var path in templates.ExtractPlaceholders(template)) Register(path, rowSource);
        }

        void Register(string path, string? rowSource)
        {
            var aggregate = System.Text.RegularExpressions.Regex.Match(path, @"^(Sum|Avg|Min|Max|Count)\s*\((.*)\)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (aggregate.Success)
            {
                string argument = aggregate.Groups[2].Value.Contains(',') ? aggregate.Groups[2].Value.Split(',')[^1].Trim() : aggregate.Groups[2].Value.Trim();
                int dot = argument.IndexOf('.');
                string? source = argument.Length == 0 ? rowSource : dot < 0 ? argument : argument[..dot];
                if (source is not null)
                {
                    var sample = Source(sources, source);
                    if (dot >= 0 && !aggregate.Groups[1].Value.Equals("count", StringComparison.OrdinalIgnoreCase))
                        sample.Fields[argument[(dot + 1)..].Trim()] = VariableType.Number;
                }
                return;
            }

            string head = path.Split('.')[0];
            if (TemplateEngine.BuiltIns.Contains(head, StringComparer.OrdinalIgnoreCase) && !path.Contains('.')) return;

            // {OtherSource.Field}: a field of a different data source (for example a header record used inside a row).
            if (sources.ContainsKey(head) && path.Contains('.') && !head.Equals(rowSource, StringComparison.OrdinalIgnoreCase))
            {
                Source(sources, head).Fields.TryAdd(path[(path.IndexOf('.') + 1)..], VariableType.Text);
                return;
            }

            if (rowSource is not null)
            {
                // {.Field}, {Source.Field} and (when no variable has that name) {Field} are fields of the current row.
                if (path.StartsWith('.')) { Source(sources, rowSource).Fields.TryAdd(path[1..], VariableType.Text); return; }
                if (head.Equals(rowSource, StringComparison.OrdinalIgnoreCase) && path.Contains('.'))
                {
                    Source(sources, rowSource).Fields.TryAdd(path[(path.IndexOf('.') + 1)..], VariableType.Text);
                    return;
                }
                if (!declared.ContainsKey(head) && !variables.ContainsKey(head))
                {
                    Source(sources, rowSource).Fields.TryAdd(path, VariableType.Text);
                    return;
                }
            }

            if (sources.ContainsKey(head) && path.Contains('.'))
            {
                Source(sources, head).Fields.TryAdd(path[(path.IndexOf('.') + 1)..], VariableType.Text);
                return;
            }
            AddPath(variables, path, declared.TryGetValue(head, out var def) && !path.Contains('.') ? SampleValue(def.Type, def.Name, 1) : $"[{path}]");
        }

        Visit(design.Elements, null);
        foreach (var band in new[] { design.ReportHeader, design.PageHeader, design.PageFooter, design.ReportFooter })
            if (band is not null) Visit(band.Elements, null);

        var data = new ReportData();
        foreach (var variable in design.Dictionary.Variables.Where(v => !string.IsNullOrWhiteSpace(v.Name)))
            variables.TryAdd(variable.Name, variable.DefaultValue is not null ? Parse(variable) : SampleValue(variable.Type, variable.Name, 1));

        foreach (var (name, sample) in sources)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var rows = new List<object?>();
            for (int i = 1; i <= SampleRows; i++)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (field, type) in sample.Fields) SetPath(row, field, SampleValue(type, field, i));
                rows.Add(row);
            }
            data.AddDataSource(name, rows);
        }

        foreach (var pair in variables) data.AddVariable(pair.Key, pair.Value);
        foreach (var key in imageKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
            data.AddImage(key, Convert.FromBase64String(PlaceholderPng));
        return data;
    }

    private sealed class SourceSample
    {
        public Dictionary<string, VariableType> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static SourceSample Source(Dictionary<string, SourceSample> sources, string name)
    {
        if (!sources.TryGetValue(name, out var sample)) sources[name] = sample = new SourceSample();
        return sample;
    }

    private static object? Parse(VariableDefinition variable) => variable.Type switch
    {
        VariableType.Number when decimal.TryParse(variable.DefaultValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) => n,
        VariableType.Date when DateTime.TryParse(variable.DefaultValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d,
        VariableType.Boolean when bool.TryParse(variable.DefaultValue, out var b) => b,
        _ => variable.DefaultValue,
    };

    /// <summary>Plain ASCII where possible, so the value also works as barcode content.</summary>
    private static object? SampleValue(VariableType type, string name, int index) => type switch
    {
        VariableType.Number => 100m * index + 0.5m,
        VariableType.Date => new DateTime(2026, 1, 1).AddDays(index * 7),
        VariableType.Boolean => index % 2 == 1,
        _ => index == 1 && !name.Contains('.') ? $"[{name}]" : $"{name} {index}",
    };

    private static void AddPath(Dictionary<string, object?> root, string path, object? value) => SetPath(root, path, value);

    private static void SetPath(Dictionary<string, object?> root, string path, object? value)
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
