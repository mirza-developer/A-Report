using AReport.Model;

namespace AReport.Designer.Services;

/// <summary>Everything the view-model needs from the window system. WPF implements it with real dialogs; tests use a fake.</summary>
public interface IDesignerShell
{
    string? PickOpenFile(string title, string filter);

    string? PickSaveFile(string title, string filter, string suggestedFileName);

    void ShowError(string title, string message);

    /// <summary>Asks whether unsaved changes may be thrown away. True = continue.</summary>
    bool ConfirmDiscardChanges();

    /// <summary>Edits a table's columns. Returns the new list, or null when cancelled.</summary>
    List<TableColumn>? EditColumns(IReadOnlyList<TableColumn> columns);

    /// <summary>Opens a file with the system's default application (used for the PDF preview).</summary>
    void OpenExternally(string path);
}

public static class FileFilters
{
    public const string Design = "A-Report design (*.art)|*.art";
    public const string Image = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
    public const string SampleData = "Sample data (*.json)|*.json";
}
