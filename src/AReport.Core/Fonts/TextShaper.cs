using System.Text;
using AReport.Model;

namespace AReport.Fonts;

/// <summary>Consecutive glyphs of one face and one bidi level, in visual (left-to-right) order.</summary>
public sealed record GlyphRun(IFace Face, IReadOnlyList<int> Codes, IReadOnlyList<double> Advances1000, string Text, int Level, IReadOnlyList<string>? Sources = null)
{
    public bool IsRightToLeft => Level % 2 == 1;
}

/// <summary>One line of shaped text: runs in visual order, ready to be positioned.</summary>
public sealed record ShapedLine(IReadOnlyList<GlyphRun> Runs, double WidthPt, string Text, bool IsRightToLeft);

/// <summary>
/// Turns a paragraph into shaped, wrapped and bidi-reordered lines:
/// digit substitution → bidi levels → Arabic shaping → font selection per character → line breaking → visual reordering.
/// </summary>
public sealed class TextShaper(FontSystem fonts)
{
    private sealed class Cluster
    {
        public int Index;
        public string Source = "";
        public int FirstCodePoint;
        public IFace Face = null!;
        public int Code;
        public double Advance1000;
        public int Level;
        public bool IsSpace;
        public bool IsCjk;
    }

    public IReadOnlyList<ShapedLine> Layout(string paragraph, FontSpec spec, TextDirection direction, DigitStyle digits,
        double maxWidthPt, Action<string>? warn = null)
    {
        var cps = ToCodePoints(paragraph, digits);
        if (cps.Count == 0) return [new ShapedLine([], 0, "", false)];

        var levels = Bidi.ResolveLevels(cps, direction, out int baseLevel);
        var units = ArabicShaper.Shape(cps);
        var clusters = BuildClusters(cps, levels, units, spec, warn);
        if (clusters.Count == 0) return [new ShapedLine([], 0, "", baseLevel % 2 == 1)];
        return Break(clusters, spec, maxWidthPt, baseLevel);
    }

    private static List<int> ToCodePoints(string text, DigitStyle digits)
    {
        var result = new List<int>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            int cp = rune.Value;
            if (cp == '\t') cp = ' ';
            if (cp is >= '0' and <= '9')
                cp = digits switch { DigitStyle.Persian => 0x06F0 + (cp - '0'), DigitStyle.Arabic => 0x0660 + (cp - '0'), _ => cp };
            result.Add(cp);
        }
        return result;
    }

    private List<Cluster> BuildClusters(List<int> cps, int[] levels, List<ShapedUnit> units, FontSpec spec, Action<string>? warn)
    {
        var clusters = new List<Cluster>(units.Count);
        IFace? previous = null;
        var missing = new SortedSet<int>();

        foreach (var unit in units)
        {
            int first = cps[unit.Start];
            if (ArabicShaper.IsInvisible(first)) continue;

            int level = levels[unit.Start];
            int glyphCp = unit.GlyphCodePoint;
            if (level % 2 == 1 && unit.Length == 1 && glyphCp == first) glyphCp = Bidi.Mirror(glyphCp);

            var face = fonts.FaceFor(spec, glyphCp, previous, warn);
            if (face is null && glyphCp != first)
            {
                glyphCp = first;
                face = fonts.FaceFor(spec, glyphCp, previous, warn);
            }

            int code = face?.CodeFor(glyphCp) ?? -1;
            if (face is null || code < 0)
            {
                missing.Add(first);
                face = fonts.Standard(spec);
                glyphCp = '?';
                code = face.CodeFor('?');
            }
            previous = face;

            var source = new StringBuilder();
            for (int k = 0; k < unit.Length; k++) source.Append(char.ConvertFromUtf32(cps[unit.Start + k]));

            clusters.Add(new Cluster
            {
                Index = clusters.Count,
                Source = source.ToString(),
                FirstCodePoint = first,
                Face = face,
                Code = code,
                Advance1000 = face.Advance1000(code),
                Level = level,
                IsSpace = first is ' ' or 0x3000 or 0x00A0,
                IsCjk = UnicodeScripts.IsCjk(first),
            });
        }
        if (missing.Count > 0)
        {
            string examples = string.Join(", ", missing.Take(4).Select(cp => $"U+{cp:X4}"));
            warn?.Invoke($"No installed font has glyphs for {missing.Count} character(s) in this text (for example {examples}); '?' is drawn instead. Install a font for this script or add its folder to ReportOptions.Fonts.Directories.");
        }
        return clusters;
    }

    private static bool CanBreakAfter(List<Cluster> clusters, int i)
    {
        var current = clusters[i];
        if (current.IsSpace) return true;
        if (i + 1 >= clusters.Count) return false;

        var next = clusters[i + 1];
        // CJK text may break between any two characters, but not before closing punctuation.
        return (current.IsCjk || next.IsCjk) && current.IsCjk && next.IsCjk && !UnicodeScripts.IsClosingPunctuation(next.FirstCodePoint);
    }

    private static IReadOnlyList<ShapedLine> Break(List<Cluster> clusters, FontSpec spec, double maxWidthPt, int baseLevel)
    {
        var lines = new List<ShapedLine>();
        double size = spec.SizePt / 1000.0;
        int start = 0;

        while (start < clusters.Count)
        {
            double width = 0;
            int lastBreak = -1, end = -1;
            for (int i = start; i < clusters.Count; i++)
            {
                var c = clusters[i];
                double w = c.Advance1000 * size;
                if (!c.IsSpace && width + w > maxWidthPt + 1e-6 && i > start)
                {
                    end = lastBreak > start ? lastBreak : i;
                    break;
                }
                width += w;
                if (CanBreakAfter(clusters, i)) lastBreak = i + 1;
            }
            if (end < 0) end = clusters.Count;

            lines.Add(MakeLine(clusters, start, end, size, baseLevel));
            start = end;
        }
        return lines;
    }

    private static ShapedLine MakeLine(List<Cluster> clusters, int start, int end, double size, int baseLevel)
    {
        while (end > start && clusters[end - 1].IsSpace) end--;
        if (end == start) return new ShapedLine([], 0, "", baseLevel % 2 == 1);

        var cps = clusters.Select(c => c.FirstCodePoint).ToArray();
        var levels = clusters.Select(c => c.Level).ToArray();
        var order = Bidi.VisualOrder(cps, levels, start, end, baseLevel);

        var runs = new List<GlyphRun>();
        double width = 0;
        int i = 0;
        while (i < order.Length)
        {
            var first = clusters[order[i]];
            int j = i;
            while (j < order.Length && clusters[order[j]].Face == first.Face && clusters[order[j]].Level == first.Level) j++;

            var members = order[i..j].Select(k => clusters[k]).ToList();
            var codes = members.Select(m => m.Code).ToList();
            var advances = members.Select(m => m.Advance1000).ToList();
            string text = string.Concat(members.OrderBy(m => m.Index).Select(m => m.Source));
            runs.Add(new GlyphRun(first.Face, codes, advances, text, first.Level, members.Select(m => m.Source).ToList()));
            width += advances.Sum() * size;
            i = j;
        }

        string logical = string.Concat(clusters.Skip(start).Take(end - start).Select(c => c.Source));
        return new ShapedLine(runs, width, logical, baseLevel % 2 == 1);
    }
}
