namespace AReport.Model;

/// <summary>Constants that identify the .art file format.</summary>
public static class ArtFormat
{
    public const string Identifier = "art";
    /// <summary>Version 2 added bands, the dictionary, shapes, barcodes, repeaters and richer text. Version 1 files load unchanged.</summary>
    public const int CurrentVersion = 2;
    public const string Extension = ".art";
    public const string MimeType = "application/vnd.areport.art+json";
}
