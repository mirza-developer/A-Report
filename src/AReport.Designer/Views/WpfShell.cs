using System.Diagnostics;
using System.Windows;
using AReport.Designer.Services;
using AReport.Model;
using Microsoft.Win32;

namespace AReport.Designer.Views;

/// <summary>Real dialogs for <see cref="IDesignerShell"/>.</summary>
public sealed class WpfShell(Window owner) : IDesignerShell
{
    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = suggestedFileName,
            AddExtension = true,
            DefaultExt = ArtFormat.Extension,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public void ShowError(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool ConfirmDiscardChanges() =>
        MessageBox.Show(owner, "You have unsaved changes. Discard them?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public List<TableColumn>? EditColumns(IReadOnlyList<TableColumn> columns)
    {
        var dialog = new ColumnsDialog(columns) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public void OpenExternally(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
