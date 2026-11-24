using System.Text.Json.Serialization;

namespace AReport.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HAlign { Left, Center, Right }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VAlign { Top, Middle, Bottom }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageFit { Stretch, Contain }

/// <summary>Direction of a paragraph. <c>Auto</c> follows the first strong character.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextDirection { Auto, LeftToRight, RightToLeft }

/// <summary>Digit shapes used when drawing the digits 0-9.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DigitStyle { Latin, Persian, Arabic }

/// <summary>Which sides of an element get a border line.</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BorderSides { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, All = 15 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShapeKind
{
    Rectangle, RoundedRectangle, Ellipse, Triangle, RightTriangle, Diamond, Pentagon, Hexagon, Star,
    ArrowRight, ArrowLeft, ArrowUp, ArrowDown,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BarcodeSymbology { QrCode, DataMatrix, Code128 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VariableType { Text, Number, Date, Boolean }
