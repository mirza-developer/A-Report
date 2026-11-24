using System.Collections;
using System.Data;
using System.Text.Json;
using AReport.Imaging;

namespace AReport.Data;

/// <summary>Everything a report needs at run time: variables, data sources and images. Names are case-insensitive.</summary>
public sealed class ReportData
{
    private readonly Dictionary<string, object?> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<object?>> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ReportImage> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _imageErrors = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> VariableNames => _variables.Keys;
    public IReadOnlyCollection<string> DataSourceNames => _sources.Keys;

    public ReportData AddVariable(string name, object? value)
    {
        _variables[RequireName(name)] = value;
        return this;
    }

    /// <summary>
    /// Adds rows. Accepts any <see cref="IEnumerable"/> (list of objects, dictionaries, <see cref="DataRow"/>…),
    /// a <see cref="DataTable"/>/<see cref="DataView"/>, or a single object/dictionary (one row).
    /// </summary>
    public ReportData AddDataSource(string name, object? source)
    {
        _sources[RequireName(name)] = ToRows(source);
        return this;
    }

    /// <summary>Registers a PNG/JPEG. Unreadable images are remembered and reported as layout warnings.</summary>
    public ReportData AddImage(string key, byte[] bytes)
    {
        RequireName(key);
        ArgumentNullException.ThrowIfNull(bytes);
        _images.Remove(key);
        _imageErrors.Remove(key);
        if (ImageReader.TryRead(bytes, out var image, out var error))
            _images[key] = image!;
        else
            _imageErrors[key] = $"Image '{key}': {error}";
        return this;
    }

    public ReportData AddImageFile(string key, string path) => AddImage(key, File.ReadAllBytes(path));

    /// <summary>A copy that can be extended (for example with design defaults) without changing the caller's data.</summary>
    public ReportData Clone()
    {
        var copy = new ReportData();
        foreach (var pair in _variables) copy._variables[pair.Key] = pair.Value;
        foreach (var pair in _sources) copy._sources[pair.Key] = pair.Value;
        foreach (var pair in _images) copy._images[pair.Key] = pair.Value;
        foreach (var pair in _imageErrors) copy._imageErrors[pair.Key] = pair.Value;
        return copy;
    }

    /// <summary>True when an image (or the reason it could not be read) is registered under the key.</summary>
    public bool HasImage(string key) => _images.ContainsKey(key) || _imageErrors.ContainsKey(key);

    public bool TryGetVariable(string name, out object? value) => _variables.TryGetValue(name, out value);

    public bool TryGetDataSource(string name, out IReadOnlyList<object?> rows)
    {
        if (_sources.TryGetValue(name, out var found))
        {
            rows = found;
            return true;
        }
        rows = [];
        return false;
    }

    public bool TryGetImage(string key, out ReportImage image)
    {
        if (_images.TryGetValue(key, out var found))
        {
            image = found;
            return true;
        }
        image = null!;
        return false;
    }

    public bool TryGetImageError(string key, out string error) => _imageErrors.TryGetValue(key, out error!);

    /// <summary>Builds data from the shapes the old <c>PdfExporter</c> used (lists of key/value pairs; images are file paths).</summary>
    public static ReportData FromLegacy(
        IEnumerable<KeyValuePair<string, object>>? dataSources,
        IEnumerable<KeyValuePair<string, string>>? images,
        IEnumerable<KeyValuePair<string, object>>? variables)
    {
        var data = new ReportData();
        foreach (var pair in dataSources ?? [])
            data.AddDataSource(pair.Key, pair.Value);
        foreach (var pair in variables ?? [])
            data.AddVariable(pair.Key, pair.Value);
        foreach (var pair in images ?? [])
        {
            // A missing file is skipped, exactly like the old exporter did.
            if (!string.IsNullOrWhiteSpace(pair.Value) && File.Exists(pair.Value))
                data.AddImageFile(pair.Key, pair.Value);
        }
        return data;
    }

    /// <summary>Loads <c>{ "variables": { … }, "dataSources": { "Items": [ { … } ] } }</c> (used for designer previews and tests).</summary>
    public static ReportData FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var data = new ReportData();
            if (root.ValueKind != JsonValueKind.Object)
                throw new ReportException("The sample data must be a JSON object.");

            if (root.TryGetProperty("variables", out var variables) && variables.ValueKind == JsonValueKind.Object)
                foreach (var property in variables.EnumerateObject())
                    data.AddVariable(property.Name, ConvertJson(property.Value));

            if (root.TryGetProperty("dataSources", out var sources) && sources.ValueKind == JsonValueKind.Object)
                foreach (var property in sources.EnumerateObject())
                    data.AddDataSource(property.Name, ConvertJson(property.Value));

            return data;
        }
        catch (JsonException ex)
        {
            throw new ReportException("The sample data is not valid JSON: " + ex.Message, ex);
        }
    }

    private static IReadOnlyList<object?> ToRows(object? source)
    {
        switch (source)
        {
            case null:
                return [];
            case string:
                throw new ArgumentException("A data source cannot be a string. Pass a list of rows.", nameof(source));
            case DataTable table:
                return table.Rows.Cast<DataRow>().Cast<object?>().ToList();
            case DataView view:
                return view.Cast<DataRowView>().Cast<object?>().ToList();
            case IDictionary:
            case IReadOnlyDictionary<string, object?>:
                return [source];
            case IEnumerable sequence:
                return sequence.Cast<object?>().ToList();
            default:
                return [source];
        }
    }

    private static object? ConvertJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ConvertJson(p.Value), StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l
            : element.TryGetDecimal(out var d) ? d
            : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static string RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A name is required.", nameof(name));
        return name;
    }
}
