using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AReport.Data;

namespace AReport.Templates;

public interface ITemplateEngine
{
    /// <summary>Replaces <c>{Name}</c>, <c>{Name.Path}</c> and <c>{Name:format}</c>. <c>{{</c> and <c>}}</c> are literal braces.</summary>
    string Render(string? template, BindingContext context, ICollection<string>? warnings = null);

    /// <summary>The distinct paths used by a template (without formats).</summary>
    IReadOnlyList<string> ExtractPlaceholders(string? template);
}

/// <summary>
/// Resolution order for a path: built-ins (<c>PageNumber</c>, <c>PageCount</c>, <c>Now</c>, <c>Line</c>), aggregates, the current repeater row
/// (<c>{.Field}</c>, <c>{Source.Field}</c> or a plain <c>{Field}</c>), then variables, then the first row of a data source.
/// </summary>
public sealed class TemplateEngine(IValueResolver resolver) : ITemplateEngine
{
    public static readonly IReadOnlyList<string> BuiltIns = ["PageNumber", "PageCount", "TotalPageCount", "Now", "Line"];

    private static readonly Regex Aggregate = new(@"^(Sum|Avg|Min|Max|Count)\s*\(\s*(.*?)\s*\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly record struct Token(string Literal, string? Path, string? Format, string Raw);

    public string Render(string? template, BindingContext context, ICollection<string>? warnings = null)
    {
        if (string.IsNullOrEmpty(template)) return "";
        ArgumentNullException.ThrowIfNull(context);

        var sb = new StringBuilder(template.Length);
        foreach (var token in Tokenize(template))
        {
            if (token.Path is null)
            {
                sb.Append(token.Literal);
                continue;
            }

            if (TryResolve(token.Path, context, out var value))
            {
                sb.Append(ValueFormatter.Format(value, token.Format, context.Culture));
                continue;
            }

            var message = $"Placeholder '{{{token.Path}}}' could not be resolved.";
            if (context.Strict) throw new ReportBindingException(message);
            warnings?.Add(message);
        }
        return sb.ToString();
    }

    public IReadOnlyList<string> ExtractPlaceholders(string? template)
    {
        if (string.IsNullOrEmpty(template)) return [];
        return Tokenize(template).Where(t => t.Path is not null).Select(t => t.Path!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool TryResolve(string path, BindingContext context, out object? value)
    {
        value = null;

        // {.Field}: a field of the current repeater row.
        if (path.StartsWith('.'))
            return context.Row is not null && resolver.TryResolve(context.Row, path[1..], out value);

        var aggregate = Aggregate.Match(path);
        if (aggregate.Success) return TryAggregate(aggregate.Groups[1].Value, aggregate.Groups[2].Value, context, out value);

        int dot = path.IndexOf('.');
        string head = (dot < 0 ? path : path[..dot]).Trim();
        string? rest = dot < 0 ? null : path[(dot + 1)..];

        if (rest is null)
        {
            if (head.Equals("PageNumber", StringComparison.OrdinalIgnoreCase)) { value = context.PageNumber; return true; }
            if (head.Equals("PageCount", StringComparison.OrdinalIgnoreCase) || head.Equals("TotalPageCount", StringComparison.OrdinalIgnoreCase)) { value = context.PageCount; return true; }
            if (head.Equals("Now", StringComparison.OrdinalIgnoreCase)) { value = context.Now; return true; }
            if (head.Equals("Line", StringComparison.OrdinalIgnoreCase) && context.Line > 0) { value = context.Line; return true; }
        }

        if (context.Row is not null)
        {
            // {Source.Field} inside a row of that source, then a plain {Field} of the current row, win over variables.
            if (rest is not null && context.RowSource is not null && head.Equals(context.RowSource, StringComparison.OrdinalIgnoreCase)
                && resolver.TryResolve(context.Row, rest, out value))
                return true;
            if (resolver.TryResolve(context.Row, path, out value)) return true;
            value = null;
        }

        if (context.Data.TryGetVariable(head, out var variable))
            return rest is null ? Assign(variable, out value) : resolver.TryResolve(variable, rest, out value);

        if (context.Data.TryGetDataSource(head, out var rows) && rest is not null)
            return rows.Count == 0 ? Assign(null, out value) : resolver.TryResolve(rows[0], rest, out value);

        return false;
    }

    /// <summary>Sum/Avg/Min/Max(Source.Field) and Count(Source) over a whole data source. Count() uses the current repeater's source.</summary>
    private bool TryAggregate(string function, string arguments, BindingContext context, out object? value)
    {
        value = null;

        // Stimulsoft-style "Sum(Band, Source.Field)": only the last argument matters.
        string argument = arguments.Contains(',') ? arguments[(arguments.LastIndexOf(',') + 1)..].Trim() : arguments;

        string? source, field = null;
        int dot = argument.IndexOf('.');
        if (argument.Length == 0) source = context.RowSource;
        else if (dot < 0) source = argument;
        else { source = argument[..dot].Trim(); field = argument[(dot + 1)..].Trim(); }

        if (source is null || !context.Data.TryGetDataSource(source, out var rows)) return false;

        if (function.Equals("Count", StringComparison.OrdinalIgnoreCase))
        {
            value = rows.Count;
            return true;
        }
        if (field is null) return false;

        var numbers = new List<decimal>();
        foreach (var row in rows)
        {
            if (!resolver.TryResolve(row, field, out var cell) || cell is null) continue;
            try
            {
                numbers.Add(Convert.ToDecimal(cell, CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                // text in a numeric column is ignored
            }
        }

        value = numbers.Count == 0 ? 0m : function.ToLowerInvariant() switch
        {
            "sum" => numbers.Sum(),
            "avg" => numbers.Average(),
            "min" => numbers.Min(),
            _ => numbers.Max(),
        };
        return true;
    }

    private static bool Assign(object? source, out object? value)
    {
        value = source;
        return true;
    }

    private static IEnumerable<Token> Tokenize(string template)
    {
        var literal = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{')
            {
                if (i + 1 < template.Length && template[i + 1] == '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    literal.Append(c);
                    i++;
                    continue;
                }

                string inner = template.Substring(i + 1, close - i - 1);
                int colon = inner.IndexOf(':');
                string path = (colon < 0 ? inner : inner[..colon]).Trim();
                string? format = colon < 0 ? null : inner[(colon + 1)..];

                if (path.Length == 0)
                {
                    literal.Append('{').Append(inner).Append('}');
                }
                else
                {
                    if (literal.Length > 0)
                    {
                        yield return new Token(literal.ToString(), null, null, "");
                        literal.Clear();
                    }
                    yield return new Token("", path, format, inner);
                }
                i = close + 1;
                continue;
            }

            if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
            {
                literal.Append('}');
                i += 2;
                continue;
            }

            literal.Append(c);
            i++;
        }

        if (literal.Length > 0)
            yield return new Token(literal.ToString(), null, null, "");
    }
}
