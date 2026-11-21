using AReport.Model;

namespace AReport.Serialization;

/// <summary>Reads and writes report designs. Implemented for the .art format by <see cref="ArtSerializer"/>.</summary>
public interface IDesignSerializer
{
    string Serialize(ReportDesign design);

    ReportDesign Deserialize(string text);

    Task SaveAsync(ReportDesign design, Stream destination, CancellationToken cancellationToken = default);

    Task<ReportDesign> LoadAsync(Stream source, CancellationToken cancellationToken = default);
}
