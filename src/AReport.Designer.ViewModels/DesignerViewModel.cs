using System.Collections.ObjectModel;
using System.Windows.Input;
using AReport.Data;
using AReport.Designer.Mvvm;
using AReport.Designer.Services;
using AReport.Model;
using AReport.Serialization;
using AReport.Services;

namespace AReport.Designer;

/// <summary>State and commands of the designer window. Contains no WPF types, so it is unit-tested.</summary>
public sealed class DesignerViewModel : ObservableObject
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.0;
    public const double SnapMm = 1.0;

    private readonly ArtSerializer _serializer = new();
    private readonly HistoryService _history = new();
    private readonly IDesignerShell _shell;
    private readonly IReportService _reports;

    private ReportDesign _design = NewDesign();
    private ReportElement? _selected;
    private string? _filePath;
    private bool _dirty;
    private double _zoom = 1;
    private string _status = "Ready";
    private ReportData? _sampleData;
    private bool _editing;
    private bool _editChanged;

    public DesignerViewModel(IDesignerShell shell, IReportService? reports = null)
    {
        _shell = shell;
        _reports = reports ?? ReportServiceFactory.Create();

        NewCommand = new RelayCommand(New);
        OpenCommand = new RelayCommand(Open);
        SaveCommand = new RelayCommand(() => _ = SaveAsync(false));
        SaveAsCommand = new RelayCommand(() => _ = SaveAsync(true));
        PreviewCommand = new RelayCommand(() => _ = PreviewAsync());
        UndoCommand = new RelayCommand(Undo, () => _history.CanUndo);
        RedoCommand = new RelayCommand(Redo, () => _history.CanRedo);
        AddElementCommand = new RelayCommand(p => AddElement(p as string ?? ""));
        DeleteCommand = new RelayCommand(DeleteSelected, () => _selected is not null);
        DuplicateCommand = new RelayCommand(DuplicateSelected, () => _selected is not null);
        BringToFrontCommand = new RelayCommand(() => Reorder(toFront: true), () => _selected is not null);
        SendToBackCommand = new RelayCommand(() => Reorder(toFront: false), () => _selected is not null);
        ZoomInCommand = new RelayCommand(() => SetZoom(Zoom * 1.25));
        ZoomOutCommand = new RelayCommand(() => SetZoom(Zoom / 1.25));
        ZoomResetCommand = new RelayCommand(() => SetZoom(1));
        LoadSampleDataCommand = new RelayCommand(LoadSampleData);
        EditColumnsCommand = new RelayCommand(EditColumns, () => _selected is TableElement);

        RebuildProperties();
    }

    // ---------- state ----------

    public ReportDesign Design
    {
        get => _design;
        private set
        {
            _design = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(PageSizeText));
        }
    }

    public ReportElement? SelectedElement => _selected;

    public ObservableCollection<PropertyRow> Properties { get; } = [];

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (Set(ref _filePath, value)) OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public bool IsDirty
    {
        get => _dirty;
        private set
        {
            if (!Set(ref _dirty, value)) return;
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(UnsavedText));
        }
    }

    public double Zoom
    {
        get => _zoom;
        private set
        {
            if (!Set(ref _zoom, value)) return;
            OnPropertyChanged(nameof(ZoomText));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string ZoomText => $"{Math.Round(Zoom * 100)} %";

    public string UnsavedText => IsDirty ? "Unsaved changes" : "";

    public string PageSizeText => $"{Design.Page.WidthMm:0.#} × {Design.Page.HeightMm:0.#} mm";

    public string SelectionText => _selected is null ? "Nothing selected" : $"{_selected.Label}: {_selected.X:0.#}, {_selected.Y:0.#} · {_selected.Width:0.#} × {_selected.Height:0.#} mm";

    public string WindowTitle
    {
        get
        {
            var name = _filePath is null ? Design.Name : Path.GetFileNameWithoutExtension(_filePath);
            return $"{(IsDirty ? "● " : "")}{name} — A-Report Designer";
        }
    }

    public bool HasSampleData => _sampleData is not null;

    /// <summary>Raised when something that affects the drawing changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the selected element changed.</summary>
    public event EventHandler? SelectionChanged;

    // ---------- commands ----------

    public ICommand NewCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveAsCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand AddElementCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand BringToFrontCommand { get; }
    public ICommand SendToBackCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ZoomResetCommand { get; }
    public ICommand LoadSampleDataCommand { get; }
    public ICommand EditColumnsCommand { get; }

    // ---------- file operations ----------

    public static ReportDesign NewDesign() => new() { Name = "Untitled report" };

    /// <summary>Call from the window's closing event. False cancels the close.</summary>
    public bool CanClose() => !IsDirty || _shell.ConfirmDiscardChanges();

    public void New()
    {
        if (!CanClose()) return;
        Load(NewDesign(), null);
        Status = "New report";
    }

    public void Open()
    {
        if (!CanClose()) return;
        var path = _shell.PickOpenFile("Open report", FileFilters.Design);
        if (path is not null) OpenFile(path);
    }

    public void OpenFile(string path)
    {
        try
        {
            Load(_serializer.LoadFromFileAsync(path).GetAwaiter().GetResult(), path);
            Status = $"Opened {Path.GetFileName(path)}";
        }
        catch (ArtFormatException ex)
        {
            _shell.ShowError("Error: cannot open the file", ex.Message + " Open a file saved by A-Report.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.ShowError("Error: cannot open the file", ex.Message);
        }
    }

    public async Task<bool> SaveAsync(bool saveAs)
    {
        var path = _filePath;
        if (saveAs || path is null)
        {
            path = _shell.PickSaveFile("Save report", FileFilters.Design, Design.Name + ArtFormat.Extension);
            if (path is null) return false;
            if (!path.EndsWith(ArtFormat.Extension, StringComparison.OrdinalIgnoreCase)) path += ArtFormat.Extension;
        }

        try
        {
            await _serializer.SaveToFileAsync(Design, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _shell.ShowError("Error: cannot save the file", ex.Message);
            return false;
        }

        FilePath = path;
        IsDirty = false;
        Status = $"Saved {Path.GetFileName(path)}";
        return true;
    }

    public async Task<string?> PreviewAsync()
    {
        try
        {
            var data = _sampleData ?? SampleData.CreateFor(Design);
            var result = await _reports.GenerateAsync(Design, data, Design.Name);
            var path = Path.Combine(Path.GetTempPath(), $"areport-preview-{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(path, result.Content);
            _shell.OpenExternally(path);
            Status = result.Warnings.Count == 0
                ? $"Preview opened ({result.PageCount} {(result.PageCount == 1 ? "page" : "pages")})"
                : $"Preview opened with {result.Warnings.Count} warning(s): {result.Warnings[0]}";
            return path;
        }
        catch (ReportException ex)
        {
            _shell.ShowError("Error: cannot create the preview", ex.Message);
            return null;
        }
    }

    public void LoadSampleData()
    {
        var path = _shell.PickOpenFile("Load sample data", FileFilters.SampleData);
        if (path is null) return;
        try
        {
            _sampleData = ReportData.FromJson(File.ReadAllText(path));
            OnPropertyChanged(nameof(HasSampleData));
            Status = $"Sample data: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is ReportException or IOException)
        {
            _shell.ShowError("Error: cannot load the sample data", ex.Message);
        }
    }

    private void Load(ReportDesign design, string? path)
    {
        Design = design;
        FilePath = path;
        IsDirty = false;
        _history.Clear();
        Select(null);
        Notify();
    }

    // ---------- editing ----------

    /// <summary>Runs one undoable change. Everything that edits the design goes through here (or BeginEdit/EndEdit).</summary>
    public void Mutate(Action change)
    {
        _history.Push(Snapshot());
        change();
        IsDirty = true;
        AfterChange();
    }

    public void Select(ReportElement? element)
    {
        if (ReferenceEquals(_selected, element)) return;
        _selected = element;
        RebuildProperties();
        OnPropertyChanged(nameof(SelectedElement));
        OnPropertyChanged(nameof(SelectionText));
        RefreshCommands();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddElement(string kind)
    {
        var step = Design.Elements.Count % 10 * 5;
        var element = ElementFactory.Create(kind, Design.Page.MarginLeftMm + step, Design.Page.MarginTopMm + step);

        if (element is ImageElement image)
        {
            var path = _shell.PickOpenFile("Choose an image", FileFilters.Image);
            if (path is null) return;
            var error = ElementFactory.Embed(image, File.ReadAllBytes(path));
            if (error is not null)
            {
                _shell.ShowError("Error: cannot use the image", error);
                return;
            }
            image.Name = Path.GetFileNameWithoutExtension(path);
        }

        Mutate(() => Design.Elements.Add(element));
        Select(element);
        Status = $"Added {element.Kind.ToLowerInvariant()}";
    }

    public void DeleteSelected()
    {
        if (_selected is null) return;
        var element = _selected;
        Mutate(() => Design.Elements.Remove(element));
        Select(null);
        Status = $"Deleted {element.Label}";
    }

    public void DuplicateSelected()
    {
        if (_selected is null) return;
        var copy = ElementFactory.Clone(_selected);
        Mutate(() => Design.Elements.Add(copy));
        Select(copy);
        Status = $"Duplicated {copy.Label}";
    }

    private void Reorder(bool toFront)
    {
        if (_selected is null) return;
        var element = _selected;
        Mutate(() =>
        {
            Design.Elements.Remove(element);
            if (toFront) Design.Elements.Add(element);
            else Design.Elements.Insert(0, element);
        });
    }

    private void EditColumns()
    {
        if (_selected is not TableElement table) return;
        var columns = _shell.EditColumns(table.Columns);
        if (columns is not null) Mutate(() => table.Columns = columns);
    }

    public void Nudge(double dxMm, double dyMm)
    {
        if (_selected is null) return;
        var element = _selected;
        Mutate(() =>
        {
            element.X += dxMm;
            element.Y += dyMm;
        });
    }

    // ---------- drag support (one undo step per gesture) ----------

    public void BeginEdit()
    {
        _history.Push(Snapshot());
        _editing = true;
        _editChanged = false;
    }

    public void ApplyGeometry(ReportElement element, double x, double y, double width, double height)
    {
        if (!_editing) return;
        if (element.X == x && element.Y == y && element.Width == width && element.Height == height) return;
        element.X = x;
        element.Y = y;
        element.Width = width;
        element.Height = height;
        _editChanged = true;
        IsDirty = true;
        AfterChange();
    }

    public void EndEdit()
    {
        if (!_editing) return;
        _editing = false;
        if (!_editChanged) _history.DiscardLast();
        RefreshCommands();
    }

    // ---------- undo / redo / zoom ----------

    public void Undo() => Restore(_history.Undo(Snapshot()));

    public void Redo() => Restore(_history.Redo(Snapshot()));

    private void Restore(string? snapshot)
    {
        if (snapshot is null) return;
        var selectedId = _selected?.Id;
        Design = _serializer.Deserialize(snapshot);
        IsDirty = true;
        _selected = null;
        Select(selectedId is null ? null : Design.Elements.FirstOrDefault(e => e.Id == selectedId));
        Notify();
    }

    public void SetZoom(double zoom) => Zoom = Math.Clamp(Math.Round(zoom, 3), MinZoom, MaxZoom);

    // ---------- internals ----------

    private string Snapshot() => _serializer.Serialize(Design);

    private void AfterChange()
    {
        foreach (var row in Properties) row.Refresh();
        OnPropertyChanged(nameof(PageSizeText));
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(WindowTitle));
        RefreshCommands();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(PageSizeText));
        OnPropertyChanged(nameof(SelectionText));
        RefreshCommands();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildProperties()
    {
        Properties.Clear();
        List<PropertyRow> rows;
        if (_selected is not null)
        {
            rows = PropertyRow.Build(_selected, Mutate, _selected is TableElement ? EditColumns : null);
        }
        else
        {
            rows = PropertyRow.Build(Design, Mutate, only: new HashSet<string> { nameof(ReportDesign.Name) });
            rows.AddRange(PropertyRow.Build(Design.Page, Mutate));
        }
        foreach (var row in rows) Properties.Add(row);
    }

    private void RefreshCommands()
    {
        foreach (var command in new[] { UndoCommand, RedoCommand, DeleteCommand, DuplicateCommand, BringToFrontCommand, SendToBackCommand, EditColumnsCommand })
            (command as RelayCommand)?.RaiseCanExecuteChanged();
    }
}
