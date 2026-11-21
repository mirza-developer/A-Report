using System.Text;
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
/// Resolution order for the first segment of a path: built-ins (<c>PageNumber</c>, <c>PageCount</c>, <c>Now</c>),
/// then variables, then the first row of a data source.
/// </summary>
public sealed class TemplateEngine(IValueResolver resolver) : ITemplateEngine
{
    public static readonly IReadOnlyList<string> BuiltIns = ["PageNumber", "PageCount", "Now"];

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
        int dot = path.IndexOf('.');
        string head = (dot < 0 ? path : path[..dot]).Trim();
        string? rest = dot < 0 ? null : path[(dot + 1)..];

        if (rest is null)
        {
            if (head.Equals("PageNumber", StringComparison.OrdinalIgnoreCase)) { value = context.PageNumber; return true; }
            if (head.Equals("PageCount", StringComparison.OrdinalIgnoreCase)) { value = context.PageCount; return true; }
            if (head.Equals("Now", StringComparison.OrdinalIgnoreCase)) { value = context.Now; return true; }
        }

        if (context.Data.TryGetVariable(head, out var variable))
            return rest is null ? Assign(variable, out value) : resolver.TryResolve(variable, rest, out value);

        if (context.Data.TryGetDataSource(head, out var rows) && rest is not null)
            return rows.Count == 0 ? Assign(null, out value) : resolver.TryResolve(rows[0], rest, out value);

        return false;
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
