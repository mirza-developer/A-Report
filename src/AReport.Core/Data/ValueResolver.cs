using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;

namespace AReport.Data;

/// <summary>Reads a (possibly dotted) path out of an object.</summary>
public interface IValueResolver
{
    /// <summary>
    /// Returns false when a member on the path does not exist. A null along the way resolves to
    /// <c>true</c> with a null value, so optional objects do not produce warnings.
    /// </summary>
    bool TryResolve(object? root, string path, out object? value);
}

/// <summary>Resolves members of POCOs, dictionaries, <see cref="DataRow"/> and <see cref="DataRowView"/>, ignoring case.</summary>
public sealed class ValueResolver : IValueResolver
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();

    public bool TryResolve(object? root, string path, out object? value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path)) return true;

        foreach (var segment in path.Split('.'))
        {
            if (value is null) return true;
            if (!TryGetMember(value, segment.Trim(), out var next))
            {
                value = null;
                return false;
            }
            value = next;
        }
        return true;
    }

    private static bool TryGetMember(object target, string name, out object? value)
    {
        value = null;
        if (name.Length == 0) return false;

        switch (target)
        {
            case IReadOnlyDictionary<string, object?> readOnly:
                if (readOnly.TryGetValue(name, out value)) return true;
                foreach (var pair in readOnly)
                {
                    if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }
                return false;

            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (entry.Key is string key && string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = entry.Value;
                        return true;
                    }
                }
                return false;

            case DataRowView view:
                return TryGetRowValue(view.Row, name, out value);

            case DataRow row:
                return TryGetRowValue(row, name, out value);
        }

        var property = PropertyCache.GetOrAdd((target.GetType(), name), static k => FindProperty(k.Item1, k.Item2));
        if (property is null) return false;
        value = property.GetValue(target);
        return true;
    }

    private static bool TryGetRowValue(DataRow row, string name, out object? value)
    {
        value = null;
        if (!row.Table.Columns.Contains(name)) return false;
        var raw = row[name];
        value = raw is DBNull ? null : raw;
        return true;
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        try
        {
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return p is { } && p.GetIndexParameters().Length == 0 ? p : null;
        }
        catch (AmbiguousMatchException)
        {
            return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
