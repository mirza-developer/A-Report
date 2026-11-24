using System.Collections.Concurrent;

namespace AReport.Fonts;

/// <summary>
/// Chooses the face that draws each character. Latin text in Helvetica/Times/Courier stays on the built-in PDF fonts;
/// everything else (Persian, Chinese, custom families) is embedded from real font files with fallback by script.
/// </summary>
public sealed class FontSystem
{
    private readonly FontLibrary? _library;
    private readonly FontOptions _options;
    private readonly ConcurrentDictionary<string, StandardFace> _standard = new();
    private readonly ConcurrentDictionary<(string, bool, bool, int), IFace?> _cache = new();

    public FontSystem(FontLibrary? library = null, FontOptions? options = null)
    {
        _library = library;
        _options = options ?? new FontOptions();
    }

    /// <summary>A system that only knows the built-in PDF fonts (Latin text).</summary>
    public static FontSystem StandardOnly { get; } = new();

    public FontLibrary? Library => _library;

    public StandardFace Standard(FontSpec spec) => _standard.GetOrAdd(StandardFonts.PdfName(spec), _ => new StandardFace(spec));

    /// <summary>The face for a character, or null when no font can draw it.</summary>
    public IFace? FaceFor(FontSpec spec, int codePoint, IFace? current, Action<string>? warn = null)
    {
        var script = UnicodeScripts.Of(codePoint);

        // Spaces, digits and punctuation stay in the font of the surrounding text.
        if (script == Script.Common && current is not null && current.Covers(codePoint)) return current;

        var key = (spec.Family.ToLowerInvariant(), spec.Bold, spec.Italic, codePoint);
        if (_cache.TryGetValue(key, out var cached) && cached is not null) return cached;

        var face = Resolve(spec, codePoint, script, warn);
        if (face is not null) _cache[key] = face;
        return face;
    }

    private IFace? Resolve(FontSpec spec, int codePoint, Script script, Action<string>? warn)
    {
        bool standardAlias = StandardFonts.IsStandardAlias(spec.Family);

        if (standardAlias)
        {
            var std = Standard(spec);
            if (std.Covers(codePoint)) return std;
        }

        if (_library is not null)
        {
            var requested = _library.Find(spec.Family, spec.Bold, spec.Italic);
            if (requested is not null && requested.Covers(codePoint)) return requested;

            if (requested is null && !standardAlias && script is not (Script.Common or Script.Latin))
                warn?.Invoke($"Font '{spec.Family}' was not found; a fallback font is used.");

            if (_options.Fallbacks.TryGetValue(script, out var names))
            {
                foreach (var name in names)
                {
                    var candidate = _library.Find(name, spec.Bold, spec.Italic);
                    if (candidate is not null && candidate.Covers(codePoint)) return candidate;
                }
            }

            // Latin text and punctuation: the built-in font is a better last resort than an arbitrary installed font.
            if (script is Script.Latin or Script.Common)
            {
                var std = Standard(spec);
                if (std.Covers(codePoint))
                {
                    if (!standardAlias && requested is null)
                        warn?.Invoke($"Font '{spec.Family}' was not found; Helvetica is used instead.");
                    return std;
                }
            }

            if (_options.SearchAllFontsForMissingGlyphs)
            {
                var any = _library.FindAnyCovering(codePoint, spec.Bold, spec.Italic);
                if (any is not null) return any;
            }
        }

        // Latin text in a font we could not find: the built-in font is better than nothing.
        var fallback = Standard(spec);
        if (fallback.Covers(codePoint))
        {
            if (!standardAlias && _library is not null)
                warn?.Invoke($"Font '{spec.Family}' was not found; Helvetica is used instead.");
            return fallback;
        }
        return null;
    }
}
