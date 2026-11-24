namespace AReport.Model;

/// <summary>What the report expects from the application: named variables, data sources with their fields, and images.</summary>
public sealed class ReportDictionary
{
    public List<VariableDefinition> Variables { get; set; } = [];

    public List<DataSourceDefinition> DataSources { get; set; } = [];

    public List<ImageResource> Images { get; set; } = [];
}

/// <summary>A variable the report uses. The default is applied when the application does not supply the variable.</summary>
public sealed class VariableDefinition
{
    public string Name { get; set; } = "";
    public VariableType Type { get; set; } = VariableType.Text;
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
}

public sealed class DataSourceDefinition
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<DataFieldDefinition> Fields { get; set; } = [];
}

public sealed class DataFieldDefinition
{
    public string Name { get; set; } = "";
    public VariableType Type { get; set; } = VariableType.Text;
}

/// <summary>An image stored in the report file and referenced by name from image elements.</summary>
public sealed class ImageResource
{
    public string Name { get; set; } = "";

    /// <summary>PNG or JPEG, base64 encoded.</summary>
    public string Base64 { get; set; } = "";
}
