namespace AReport.Fonts;

/// <summary>Where fonts come from and which fonts to try when the requested one cannot draw a character.</summary>
public sealed class FontOptions
{
    /// <summary>Extra folders with .ttf/.ttc files (searched before the system fonts). Use this on servers without fonts.</summary>
    public IList<string> Directories { get; } = [];

    /// <summary>Also use the fonts installed on the machine.</summary>
    public bool UseSystemFonts { get; set; } = true;

    /// <summary>When no listed font has a character, look through every installed font for one that does.</summary>
    public bool SearchAllFontsForMissingGlyphs { get; set; } = true;

    /// <summary>Font families to try, in order, for each script when the requested font lacks a character.</summary>
    public IDictionary<Script, IList<string>> Fallbacks { get; } = new Dictionary<Script, IList<string>>
    {
        [Script.Arabic] = new List<string>
        {
            "Tahoma", "Segoe UI", "Arial", "Times New Roman", "Vazirmatn", "Vazir", "IRANSans", "Noto Sans Arabic", "Noto Naskh Arabic",
            "DejaVu Sans", "FreeSerif", "Arial Unicode MS",
        },
        [Script.Cjk] = new List<string>
        {
            "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "SimHei", "NSimSun", "PingFang SC", "Heiti SC", "STSong", "Noto Sans SC",
            "WenQuanYi Zen Hei", "WenQuanYi Micro Hei", "Source Han Sans SC", "Arial Unicode MS", "Malgun Gothic", "MS Gothic",
        },
        [Script.Latin] = new List<string> { "Segoe UI", "Arial", "DejaVu Sans", "Liberation Sans", "FreeSans" },
        [Script.Other] = new List<string> { "Segoe UI", "Arial", "DejaVu Sans", "Arial Unicode MS", "FreeSerif" },
        [Script.Common] = new List<string> { "Segoe UI", "Arial", "DejaVu Sans" },
    };
}
