using AReport.Model;
using AReport.Serialization;

namespace AReport.Services;

/// <summary>Finds report designs by name. Replace it to load designs from a database or blob storage.</summary>
public interface IDesignSource
{
    Task<ReportDesign> LoadAsync(string reportName, CancellationToken cancellationToken = default);
}

/// <summary>Loads <c>{ReportsDirectory}/{name}.art</c>. Names can never escape the reports directory.</summary>
public sealed class FileSystemDesignSource(ReportOptions options, IDesignSerializer serializer) : IDesignSource
{
    public async Task<ReportDesign> LoadAsync(string reportName, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(reportName);
        if (!File.Exists(path))
            throw new ReportNotFoundException(reportName);

        await using var stream = File.OpenRead(path);
        return await serializer.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    internal string ResolvePath(string reportName)
    {
        if (string.IsNullOrWhiteSpace(reportName))
            throw new ArgumentException("A report name is required.", nameof(reportName));

        string fileName = reportName.EndsWith(Model.ArtFormat.Extension, StringComparison.OrdinalIgnoreCase)
            ? reportName
            : reportName + Model.ArtFormat.Extension;

        string root = Path.GetFullPath(options.ReportsDirectory);
        string full = Path.GetFullPath(Path.Combine(root, fileName));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!full.StartsWith(prefix, comparison))
            throw new ArgumentException("The report name points outside the reports directory.", nameof(reportName));
        return full;
    }
}
