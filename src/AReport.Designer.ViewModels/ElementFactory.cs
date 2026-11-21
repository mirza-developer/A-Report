using AReport.Imaging;
using AReport.Model;
using AReport.Serialization;

namespace AReport.Designer;

/// <summary>Creates and copies elements for the designer.</summary>
public static class ElementFactory
{
    public static readonly IReadOnlyList<string> Kinds = ["text", "rectangle", "line", "image", "table"];

    public static ReportElement Create(string kind, double x, double y) => kind switch
    {
        "text" => new TextElement { Name = "Text", Text = "Text", X = x, Y = y },
        "rectangle" => new RectangleElement { Name = "Rectangle", X = x, Y = y },
        "line" => new LineElement { Name = "Line", X = x, Y = y },
        "image" => new ImageElement { Name = "Image", X = x, Y = y },
        "table" => new TableElement
        {
            Name = "Table",
            X = x,
            Y = y,
            DataSource = "Items",
            Columns =
            [
                new TableColumn { Header = "Description", Binding = "Description", Weight = 3 },
                new TableColumn { Header = "Amount", Binding = "Amount", Weight = 1, Alignment = HAlign.Right, Format = "N2" },
            ],
        },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown element kind."),
    };

    /// <summary>A deep copy with a new id, moved by <paramref name="offsetMm"/>.</summary>
    public static ReportElement Clone(ReportElement element, double offsetMm = 5)
    {
        var serializer = new ArtSerializer();
        var wrapper = serializer.Deserialize(serializer.Serialize(new ReportDesign { Elements = [element] }));
        var copy = wrapper.Elements[0];
        copy.Id = Guid.NewGuid().ToString("N");
        copy.X += offsetMm;
        copy.Y += offsetMm;
        return copy;
    }

    /// <summary>Reads a PNG/JPEG file into an image element's embedded data. Returns an error message on failure.</summary>
    public static string? Embed(ImageElement element, byte[] bytes)
    {
        if (!ImageReader.TryRead(bytes, out var image, out var error))
            return error;

        element.EmbeddedImageBase64 = Convert.ToBase64String(bytes);
        // Keep the aspect ratio of the picture: 40 mm wide.
        element.Width = 40;
        element.Height = Math.Round(40.0 * image!.HeightPx / image.WidthPx, 1);
        return null;
    }
}
