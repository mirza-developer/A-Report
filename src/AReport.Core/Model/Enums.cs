using System.Text.Json.Serialization;

namespace AReport.Model;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HAlign { Left, Center, Right }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VAlign { Top, Middle, Bottom }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImageFit { Stretch, Contain }
