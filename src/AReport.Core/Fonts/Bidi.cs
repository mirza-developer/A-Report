using AReport.Model;

namespace AReport.Fonts;

internal enum BidiClass : byte { L, R, AL, EN, ES, ET, AN, CS, NSM, BN, B, S, WS, ON, LRE, LRO, RLE, RLO, PDF, LRI, RLI, FSI, PDI }

/// <summary>
/// The Unicode Bidirectional Algorithm (UAX #9) without explicit embeddings/isolates and without bracket pairing (rule N0):
/// enough for Persian, Arabic and Hebrew mixed with Latin text and numbers.
/// </summary>
internal static class Bidi
{
    public static BidiClass ClassOf(int codePoint)
    {
        if (codePoint > 0xFFFF) return BidiClass.L;
        int lo = 0, hi = UnicodeData.BidiStarts.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (UnicodeData.BidiStarts[mid] <= codePoint) lo = mid; else hi = mid - 1;
        }
        return (BidiClass)UnicodeData.BidiClasses[lo];
    }

    /// <summary>Resolves an embedding level for each code point of one paragraph.</summary>
    public static int[] ResolveLevels(IReadOnlyList<int> codePoints, TextDirection direction, out int baseLevel)
    {
        int n = codePoints.Count;
        var original = new BidiClass[n];
        for (int i = 0; i < n; i++) original[i] = ClassOf(codePoints[i]);

        // Embedding/override controls are ignored (treated like BN); isolates act as neutrals.
        var types = (BidiClass[])original.Clone();
        for (int i = 0; i < n; i++)
        {
            if (types[i] is BidiClass.LRE or BidiClass.LRO or BidiClass.RLE or BidiClass.RLO or BidiClass.PDF) types[i] = BidiClass.BN;
            else if (types[i] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI) types[i] = BidiClass.ON;
        }

        baseLevel = direction switch
        {
            TextDirection.LeftToRight => 0,
            TextDirection.RightToLeft => 1,
            _ => FirstStrongLevel(types),
        };

        var levels = new int[n];
        var work = new List<int>(n);
        for (int i = 0; i < n; i++) if (types[i] != BidiClass.BN) work.Add(i);

        var t = work.Select(i => types[i]).ToArray();
        var sos = baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;

        ApplyWeakRules(t, sos);
        ApplyBracketRule(t, work.Select(i => codePoints[i]).ToArray(), sos, baseLevel);
        ApplyNeutralRules(t, sos, baseLevel);

        for (int k = 0; k < t.Length; k++)
        {
            int level = baseLevel;
            bool odd = baseLevel % 2 == 1;
            switch (t[k])
            {
                case BidiClass.L: if (odd) level++; break;
                case BidiClass.R: if (!odd) level++; break;
                case BidiClass.AN or BidiClass.EN: level += odd ? 1 : 2; break;
            }
            levels[work[k]] = level;
        }

        // BN characters take the level of the preceding character.
        int previous = baseLevel;
        for (int i = 0; i < n; i++)
        {
            if (types[i] == BidiClass.BN) levels[i] = previous;
            else previous = levels[i];
        }
        return levels;
    }

    private static int FirstStrongLevel(BidiClass[] types)
    {
        foreach (var type in types)
        {
            if (type == BidiClass.L) return 0;
            if (type is BidiClass.R or BidiClass.AL) return 1;
        }
        return 0;
    }

    private static void ApplyWeakRules(BidiClass[] t, BidiClass sos)
    {
        int n = t.Length;

        // W1: NSM takes the type of the previous character.
        for (int i = 0; i < n; i++)
            if (t[i] == BidiClass.NSM) t[i] = i == 0 ? sos : t[i - 1];

        // W2: EN after AL becomes AN. W3: AL becomes R.
        var lastStrong = sos;
        for (int i = 0; i < n; i++)
        {
            if (t[i] is BidiClass.L or BidiClass.R or BidiClass.AL) lastStrong = t[i];
            else if (t[i] == BidiClass.EN && lastStrong == BidiClass.AL) t[i] = BidiClass.AN;
        }
        for (int i = 0; i < n; i++) if (t[i] == BidiClass.AL) t[i] = BidiClass.R;

        // W4: a single separator between two numbers of the same kind joins them.
        for (int i = 1; i < n - 1; i++)
        {
            if (t[i] == BidiClass.ES && t[i - 1] == BidiClass.EN && t[i + 1] == BidiClass.EN) t[i] = BidiClass.EN;
            else if (t[i] == BidiClass.CS && t[i - 1] == BidiClass.EN && t[i + 1] == BidiClass.EN) t[i] = BidiClass.EN;
            else if (t[i] == BidiClass.CS && t[i - 1] == BidiClass.AN && t[i + 1] == BidiClass.AN) t[i] = BidiClass.AN;
        }

        // W5: a run of ET next to EN becomes EN.
        for (int i = 0; i < n; i++)
        {
            if (t[i] != BidiClass.ET) continue;
            int end = i;
            while (end < n && t[end] == BidiClass.ET) end++;
            bool adjacent = (i > 0 && t[i - 1] == BidiClass.EN) || (end < n && t[end] == BidiClass.EN);
            if (adjacent) for (int k = i; k < end; k++) t[k] = BidiClass.EN;
            i = end - 1;
        }

        // W6: remaining separators and terminators become neutral.
        for (int i = 0; i < n; i++)
            if (t[i] is BidiClass.ES or BidiClass.ET or BidiClass.CS) t[i] = BidiClass.ON;

        // W7: EN after L becomes L.
        lastStrong = sos;
        for (int i = 0; i < n; i++)
        {
            if (t[i] is BidiClass.L or BidiClass.R) lastStrong = t[i];
            else if (t[i] == BidiClass.EN && lastStrong == BidiClass.L) t[i] = BidiClass.L;
        }
    }

    private static readonly Dictionary<int, int> BracketPairs = new() { ['('] = ')', ['['] = ']', ['{'] = '}' };

    /// <summary>N0: a bracket pair takes the direction of its contents when they match the text direction, or the context otherwise.</summary>
    private static void ApplyBracketRule(BidiClass[] t, int[] cps, BidiClass sos, int baseLevel)
    {
        int n = t.Length;
        var embedding = baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;
        var stack = new List<(int Closing, int Index)>();
        var pairs = new List<(int Open, int Close)>();

        for (int i = 0; i < n; i++)
        {
            if (t[i] != BidiClass.ON) continue;
            if (BracketPairs.TryGetValue(cps[i], out var closing)) stack.Add((closing, i));
            else if (cps[i] is ')' or ']' or '}')
            {
                for (int k = stack.Count - 1; k >= 0; k--)
                {
                    if (stack[k].Closing != cps[i]) continue;
                    pairs.Add((stack[k].Index, i));
                    stack.RemoveRange(k, stack.Count - k);
                    break;
                }
            }
        }

        static BidiClass Strong(BidiClass c) => c switch { BidiClass.L => BidiClass.L, BidiClass.R or BidiClass.EN or BidiClass.AN => BidiClass.R, _ => BidiClass.ON };

        foreach (var (open, close) in pairs.OrderBy(p => p.Open))
        {
            bool foundEmbedding = false, foundOpposite = false;
            for (int k = open + 1; k < close; k++)
            {
                var strong = Strong(t[k]);
                if (strong == BidiClass.ON) continue;
                if (strong == embedding) { foundEmbedding = true; break; }
                foundOpposite = true;
            }

            BidiClass? resolved = null;
            if (foundEmbedding) resolved = embedding;
            else if (foundOpposite)
            {
                var context = sos;
                for (int k = open - 1; k >= 0; k--)
                {
                    var strong = Strong(t[k]);
                    if (strong != BidiClass.ON) { context = strong; break; }
                }
                resolved = context != embedding ? context : embedding;
            }
            if (resolved is { } r) t[open] = t[close] = r;
        }
    }

    private static void ApplyNeutralRules(BidiClass[] t, BidiClass sos, int baseLevel)
    {
        int n = t.Length;
        var embedding = baseLevel % 2 == 0 ? BidiClass.L : BidiClass.R;

        static BidiClass Direction(BidiClass c) => c is BidiClass.L ? BidiClass.L : BidiClass.R; // EN/AN count as R

        for (int i = 0; i < n; i++)
        {
            if (!IsNeutral(t[i])) continue;
            int end = i;
            while (end < n && IsNeutral(t[end])) end++;

            var before = i == 0 ? sos : Direction(t[i - 1]);
            var after = end == n ? sos : Direction(t[end]);
            var resolved = before == after ? before : embedding;
            for (int k = i; k < end; k++) t[k] = resolved;
            i = end - 1;
        }
    }

    private static bool IsNeutral(BidiClass c) => c is BidiClass.B or BidiClass.S or BidiClass.WS or BidiClass.ON;

    /// <summary>
    /// Visual order of the characters [start, end) of a line (rules L1 and L2). Returns logical indices left to right.
    /// </summary>
    public static int[] VisualOrder(IReadOnlyList<int> codePoints, int[] levels, int start, int end, int baseLevel)
    {
        int length = end - start;
        var lineLevels = new int[length];
        for (int i = 0; i < length; i++) lineLevels[i] = levels[start + i];

        // L1: trailing whitespace and separators go back to the paragraph level.
        for (int i = length - 1; i >= 0; i--)
        {
            var c = ClassOf(codePoints[start + i]);
            if (c is BidiClass.WS or BidiClass.S or BidiClass.B or BidiClass.BN or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI)
                lineLevels[i] = baseLevel;
            else break;
        }

        var order = Enumerable.Range(start, length).ToArray();
        if (length == 0) return order;

        int max = lineLevels.Max();
        int lowestOdd = lineLevels.Where(l => l % 2 == 1).DefaultIfEmpty(int.MaxValue).Min();
        for (int level = max; level >= lowestOdd && level > 0; level--)
        {
            for (int i = 0; i < length; i++)
            {
                if (lineLevels[i] < level) continue;
                int run = i;
                while (run < length && lineLevels[run] >= level) run++;
                Array.Reverse(order, i, run - i);
                Array.Reverse(lineLevels, i, run - i);
                i = run - 1;
            }
        }
        return order;
    }

    private static readonly Dictionary<int, int> Mirrors = new()
    {
        ['('] = ')', [')'] = '(', ['['] = ']', [']'] = '[', ['{'] = '}', ['}'] = '{', ['<'] = '>', ['>'] = '<',
        ['\u00AB'] = '\u00BB', ['\u00BB'] = '\u00AB', ['\u2039'] = '\u203A', ['\u203A'] = '\u2039',
    };

    /// <summary>The mirrored counterpart of a bracket-like character (itself when it has none).</summary>
    public static int Mirror(int codePoint) => Mirrors.TryGetValue(codePoint, out var m) ? m : codePoint;
}
