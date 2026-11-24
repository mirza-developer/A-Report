namespace AReport.Fonts;

/// <summary>One glyph position produced by shaping: the source characters it covers and the code point to look up in the font.</summary>
internal readonly record struct ShapedUnit(int Start, int Length, int GlyphCodePoint);

/// <summary>
/// Contextual shaping for Arabic-script text (Arabic, Persian, Urdu…) using the Unicode presentation forms.
/// Needs a font whose character map contains the Arabic Presentation Forms-A/B blocks (Tahoma, Arial, Segoe UI, DejaVu, …).
/// </summary>
internal static class ArabicShaper
{
    private const int Zwnj = 0x200C, Zwj = 0x200D;
    private static readonly Dictionary<int, UnicodeData.ArabicForm> Forms = UnicodeData.ArabicForms.ToDictionary(f => f.Base);
    private static readonly HashSet<int> Marks = new(UnicodeData.ArabicMarks);
    private static readonly Dictionary<int, (int Isolated, int Final)> LamAlef = UnicodeData.LamAlef.ToDictionary(l => l.Alef, l => (l.Isolated, l.Final));

    private enum Join { None, Dual, Right, Transparent }

    public static bool IsArabicScript(int cp) =>
        cp is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0x08A0 and <= 0x08FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF;

    /// <summary>True for characters that never draw anything themselves (joiners, directional marks, …).</summary>
    public static bool IsInvisible(int cp) =>
        cp is Zwnj or Zwj or 0x200E or 0x200F or >= 0x202A and <= 0x202E or >= 0x2066 and <= 0x2069 or 0xFEFF;

    private static Join JoinOf(int cp)
    {
        if (cp == Zwnj) return Join.None;
        if (cp == Zwj) return Join.Dual;
        if (Marks.Contains(cp)) return Join.Transparent;
        if (Forms.TryGetValue(cp, out var f)) return f.Joining switch { 'D' => Join.Dual, 'R' => Join.Right, _ => Join.None };
        return Join.None;
    }

    public static List<ShapedUnit> Shape(IReadOnlyList<int> cps)
    {
        int n = cps.Count;
        var join = new Join[n];
        for (int i = 0; i < n; i++) join[i] = JoinOf(cps[i]);

        int PreviousBase(int i) { for (int k = i - 1; k >= 0; k--) if (join[k] != Join.Transparent) return k; return -1; }
        int NextBase(int i) { for (int k = i + 1; k < n; k++) if (join[k] != Join.Transparent) return k; return -1; }

        var result = new List<ShapedUnit>(n);
        for (int i = 0; i < n; i++)
        {
            int cp = cps[i];
            if (join[i] is Join.None or Join.Transparent || !Forms.TryGetValue(cp, out var form) && cp != Zwj)
            {
                result.Add(new ShapedUnit(i, 1, cp));
                continue;
            }
            if (cp == Zwj)
            {
                result.Add(new ShapedUnit(i, 1, cp));
                continue;
            }

            int prev = PreviousBase(i), next = NextBase(i);
            bool joinsPrevious = prev >= 0 && join[prev] == Join.Dual;
            bool joinsNext = join[i] == Join.Dual && next >= 0 && join[next] is Join.Dual or Join.Right;

            // Lam + alef form a single ligature glyph.
            if (cp == 0x0644 && next == i + 1 && LamAlef.TryGetValue(cps[next], out var lig))
            {
                int glyph = joinsPrevious && lig.Final != 0 ? lig.Final : lig.Isolated;
                if (glyph != 0)
                {
                    result.Add(new ShapedUnit(i, 2, glyph));
                    i++;
                    continue;
                }
            }

            int chosen = (join[i], joinsPrevious, joinsNext) switch
            {
                (Join.Dual, true, true) => form.Medial,
                (Join.Dual, true, false) => form.Final,
                (Join.Dual, false, true) => form.Initial,
                (Join.Right, true, _) => form.Final,
                _ => form.Isolated,
            };
            result.Add(new ShapedUnit(i, 1, chosen != 0 ? chosen : cp));
        }
        return result;
    }
}
