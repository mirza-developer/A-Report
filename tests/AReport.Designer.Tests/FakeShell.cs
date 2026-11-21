using AReport.Designer.Services;
using AReport.Model;

namespace AReport.Designer.Tests;

internal sealed class FakeShell : IDesignerShell, IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "areport-designer-" + Guid.NewGuid().ToString("N"));
    public Queue<string?> OpenAnswers { get; } = new();
    public Queue<string?> SaveAnswers { get; } = new();
    public bool ConfirmDiscardAnswer { get; set; } = true;
    public List<(string Title, string Message)> Errors { get; } = [];
    public List<string> OpenedExternally { get; } = [];
    public int ConfirmCalls { get; private set; }
    public Func<IReadOnlyList<TableColumn>, List<TableColumn>?> ColumnEditor { get; set; } = _ => null;

    public FakeShell() => System.IO.Directory.CreateDirectory(Directory);

    public string Path_(string name) => Path.Combine(Directory, name);

    public string? PickOpenFile(string title, string filter) => OpenAnswers.Count > 0 ? OpenAnswers.Dequeue() : null;

    public string? PickSaveFile(string title, string filter, string suggestedFileName) => SaveAnswers.Count > 0 ? SaveAnswers.Dequeue() : null;

    public void ShowError(string title, string message) => Errors.Add((title, message));

    public bool ConfirmDiscardChanges()
    {
        ConfirmCalls++;
        return ConfirmDiscardAnswer;
    }

    public List<TableColumn>? EditColumns(IReadOnlyList<TableColumn> columns) => ColumnEditor(columns);

    public void OpenExternally(string path) => OpenedExternally.Add(path);

    public void Dispose() => System.IO.Directory.Delete(Directory, true);
}
