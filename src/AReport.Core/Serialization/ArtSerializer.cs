using System.Text.Json;
using AReport.Model;

namespace AReport.Serialization;

/// <summary>.art = UTF-8 JSON with <c>"format": "art"</c> and a <c>schemaVersion</c>.</summary>
public sealed class ArtSerializer : IDesignSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string Serialize(ReportDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        design.Format = ArtFormat.Identifier;
        design.SchemaVersion = ArtFormat.CurrentVersion;
        return JsonSerializer.Serialize(design, Options);
    }

    public ReportDesign Deserialize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return Validate(JsonSerializer.Deserialize<ReportDesign>(text.TrimStart('\uFEFF'), Options));
        }
        catch (JsonException ex)
        {
            throw new ArtFormatException("The file is not valid .art JSON: " + ex.Message, ex);
        }
    }

    public async Task SaveAsync(ReportDesign design, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(design);
        ArgumentNullException.ThrowIfNull(destination);
        design.Format = ArtFormat.Identifier;
        design.SchemaVersion = ArtFormat.CurrentVersion;
        await JsonSerializer.SerializeAsync(destination, design, Options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReportDesign> LoadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var design = await JsonSerializer.DeserializeAsync<ReportDesign>(source, Options, cancellationToken).ConfigureAwait(false);
            return Validate(design);
        }
        catch (JsonException ex)
        {
            throw new ArtFormatException("The file is not valid .art JSON: " + ex.Message, ex);
        }
    }

    /// <summary>Convenience wrapper for the designer and tools.</summary>
    public async Task SaveToFileAsync(ReportDesign design, string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await SaveAsync(design, stream, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReportDesign> LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static ReportDesign Validate(ReportDesign? design)
    {
        if (design is null)
            throw new ArtFormatException("The file is empty.");
        if (!string.Equals(design.Format, ArtFormat.Identifier, StringComparison.OrdinalIgnoreCase))
            throw new ArtFormatException($"Not an .art document (format is '{design.Format}').");
        if (design.SchemaVersion < 1)
            throw new ArtFormatException($"Invalid schema version {design.SchemaVersion}.");
        if (design.SchemaVersion > ArtFormat.CurrentVersion)
            throw new ArtFormatException(
                $"The file uses schema version {design.SchemaVersion}, but this A-Report supports up to {ArtFormat.CurrentVersion}. Update the package.");
        design.Page ??= new PageSettings();
        design.Elements ??= [];
        return design;
    }
}
