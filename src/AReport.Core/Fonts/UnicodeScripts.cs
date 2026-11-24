namespace AReport.Fonts;

/// <summary>Coarse script groups: enough to pick fonts and to decide where lines may break.</summary>
public enum Script { Common, Latin, Arabic, Cjk, Other }

public static class UnicodeScripts
{
    public static Script Of(int cp)
    {
        if (cp < 0x80) return char.IsLetter((char)cp) ? Script.Latin : Script.Common;
        if (ArabicShaper.IsArabicScript(cp)) return Script.Arabic;
        if (IsCjk(cp)) return Script.Cjk;
        if (cp is >= 0x00A0 and <= 0x024F or >= 0x1E00 and <= 0x1EFF)
            return char.IsLetter((char)cp) ? Script.Latin : Script.Common;
        if (cp is >= 0x2000 and <= 0x206F or >= 0x20A0 and <= 0x20CF or >= 0x2100 and <= 0x218F or >= 0x2190 and <= 0x2BFF)
            return Script.Common;
        return Script.Other;
    }

    public static bool IsCjk(int cp) =>
        cp is >= 0x2E80 and <= 0x2FDF or >= 0x3000 and <= 0x30FF or >= 0x3100 and <= 0x312F or >= 0x3130 and <= 0x318F
            or >= 0x31C0 and <= 0x31EF or >= 0x3200 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF
            or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFFEF or >= 0x20000 and <= 0x2FA1F;

    /// <summary>CJK punctuation that must not start a line.</summary>
    public static bool IsClosingPunctuation(int cp) =>
        cp is '\u3001' or '\u3002' or '\uFF0C' or '\uFF0E' or '\uFF01' or '\uFF1F' or '\uFF1B' or '\uFF1A'
            or '\u300D' or '\u300F' or '\uFF09' or '\u3011' or '\u3009' or '\u300B' or '\u30FC';
}
