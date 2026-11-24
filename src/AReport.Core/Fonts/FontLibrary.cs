using System.Collections.Concurrent;

namespace AReport.Fonts;

/// <summary>A font face found on disk.</summary>
public sealed record FontEntry(string Path, int FaceIndex, FontNames Names);

/// <summary>Finds installed and application-supplied fonts by family name. Scanning happens once, on first use.</summary>
public sealed class FontLibrary
{
    private static readonly string[] Extensions = [".ttf", ".ttc", ".otf", ".otc"];

    private readonly FontOptions _options;
    private readonly Lazy<IReadOnlyList<FontEntry>> _entries;
    private readonly ConcurrentDictionary<string, TrueTypeFont> _loaded = new();
    private readonly ConcurrentDictionary<string, TrueTypeFace?> _faces = new();
    private readonly ConcurrentDictionary<int, FontEntry?> _anyCovering = new();

    public FontLibrary(FontOptions? options = null)
    {
        _options = options ?? new FontOptions();
        _entries = new Lazy<IReadOnlyList<FontEntry>>(Scan);
    }

    /// <summary>Every TrueType-outline face that can be embedded.</summary>
    public IReadOnlyList<FontEntry> Entries => _entries.Value;

    public IEnumerable<string> FamilyNames => Entries.Select(e => e.Names.Family).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    /// <summary>Best face of a family for the requested style (exact style, else the closest, flagged for synthetic bold/italic).</summary>
    public TrueTypeFace? Find(string family, bool bold, bool italic)
    {
        if (string.IsNullOrWhiteSpace(family)) return null;
        var candidates = Entries.Where(e => Matches(e, family)).ToList();
        if (candidates.Count == 0) return null;

        var best = candidates
            .OrderBy(e => (e.Names.Bold == bold ? 0 : 4) + (e.Names.Italic == italic ? 0 : 2) + Math.Abs(e.Names.Weight - (bold ? 700 : 400)) / 1000.0)
            .First();
        return FaceFor(best, bold && !best.Names.Bold, italic && !best.Names.Italic);
    }

    /// <summary>Any font that has a glyph for the code point (last resort).</summary>
    public TrueTypeFace? FindAnyCovering(int codePoint, bool bold, bool italic)
    {
        var entry = _anyCovering.GetOrAdd(codePoint, cp =>
        {
            foreach (var e in Entries)
            {
                try
                {
                    if (Load(e).Covers(cp)) return e;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or IndexOutOfRangeException or ArgumentException)
                {
                    // unreadable font: try the next one
                }
            }
            return null;
        });
        return entry is null ? null : FaceFor(entry, bold && !entry.Names.Bold, italic && !entry.Names.Italic);
    }

    private TrueTypeFace? FaceFor(FontEntry entry, bool syntheticBold, bool syntheticItalic)
    {
        string key = $"{entry.Path}|{entry.FaceIndex}|{(syntheticBold ? 'b' : '-')}{(syntheticItalic ? 'i' : '-')}";
        return _faces.GetOrAdd(key, k =>
        {
            try
            {
                return new TrueTypeFace(Load(entry), k, syntheticBold, syntheticItalic);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or IndexOutOfRangeException or ArgumentException)
            {
                return null;
            }
        });
    }

    internal TrueTypeFont Load(FontEntry entry) =>
        _loaded.GetOrAdd($"{entry.Path}|{entry.FaceIndex}", _ => TrueTypeFont.Load(File.ReadAllBytes(entry.Path), entry.FaceIndex));

    private static bool Matches(FontEntry entry, string family)
    {
        var wanted = family.Trim();
        if (Same(entry.Names.Family, wanted) || Same(entry.Names.FullName, wanted) || entry.Names.FamilyAliases.Any(a => Same(a, wanted))) return true;

        // "IRANSans(FaNum)" and "IRANSans" are the same family for our purposes.
        int paren = wanted.IndexOf('(');
        return paren > 0 && Same(entry.Names.Family, wanted[..paren].Trim());
    }

    private static bool Same(string a, string b) => a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<FontEntry> Scan()
    {
        var directories = new List<string>(_options.Directories);
        if (_options.UseSystemFonts) directories.AddRange(SystemFontDirectories());

        var found = new List<FontEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive };

        foreach (var directory in directories.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", enumeration).Where(f => Extensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!seen.Add(System.IO.Path.GetFullPath(file))) continue;
                IReadOnlyList<FontNames> faces;
                try
                {
                    faces = TrueTypeFont.ReadNames(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                for (int i = 0; i < faces.Count; i++)
                    if (faces[i].HasGlyphOutlines && faces[i].Family.Length > 0)
                        found.Add(new FontEntry(file, i, faces[i]));
            }
        }
        return found;
    }

    private static IEnumerable<string> SystemFontDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/System/Library/Fonts";
            yield return "/Library/Fonts";
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts");
        }
        else
        {
            yield return "/usr/share/fonts";
            yield return "/usr/local/share/fonts";
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts");
            yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "fonts");
        }
    }
}
