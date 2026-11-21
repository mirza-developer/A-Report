using AReport.Model;
using AReport.Serialization;

namespace AReport.Designer.Tests;

public class DesignerViewModelTests : IDisposable
{
    private readonly FakeShell _shell = new();
    private readonly DesignerViewModel _vm;

    public DesignerViewModelTests() => _vm = new DesignerViewModel(_shell);

    public void Dispose() => _shell.Dispose();

    private static bool Can(System.Windows.Input.ICommand command) => command.CanExecute(null);

    // ---------- adding, selecting, deleting ----------

    [Fact]
    public void Starts_with_an_empty_untitled_design()
    {
        Assert.Empty(_vm.Design.Elements);
        Assert.False(_vm.IsDirty);
        Assert.Equal("Untitled report — A-Report Designer", _vm.WindowTitle);
        Assert.Equal("", _vm.UnsavedText);
    }

    [Theory]
    [InlineData("text", typeof(TextElement))]
    [InlineData("rectangle", typeof(RectangleElement))]
    [InlineData("line", typeof(LineElement))]
    [InlineData("table", typeof(TableElement))]
    public void Adding_an_element_selects_it_and_marks_the_design_dirty(string kind, Type expected)
    {
        _vm.AddElement(kind);

        Assert.Single(_vm.Design.Elements);
        Assert.IsType(expected, _vm.SelectedElement);
        Assert.Same(_vm.Design.Elements[0], _vm.SelectedElement);
        Assert.True(_vm.IsDirty);
        Assert.Equal("Unsaved changes", _vm.UnsavedText);
        Assert.StartsWith("● ", _vm.WindowTitle);
    }

    [Fact]
    public void New_elements_are_placed_at_the_margin_and_cascade()
    {
        _vm.AddElement("text");
        _vm.AddElement("text");

        Assert.Equal((10.0, 10.0), (_vm.Design.Elements[0].X, _vm.Design.Elements[0].Y));
        Assert.Equal((15.0, 15.0), (_vm.Design.Elements[1].X, _vm.Design.Elements[1].Y));
    }

    [Fact]
    public void Adding_an_image_embeds_the_picked_file_and_keeps_its_aspect_ratio()
    {
        var path = _shell.Path_("logo.png");
        File.WriteAllBytes(path, AReport.Core.Tests.Support.TestImages.Png(200, 100, 2, (_, _) => [1, 2, 3]));
        _shell.OpenAnswers.Enqueue(path);

        _vm.AddElement("image");

        var image = Assert.IsType<ImageElement>(Assert.Single(_vm.Design.Elements));
        Assert.Equal("logo", image.Name);
        Assert.False(string.IsNullOrEmpty(image.EmbeddedImageBase64));
        Assert.Equal(40, image.Width);
        Assert.Equal(20, image.Height);
    }

    [Fact]
    public void Cancelling_the_image_picker_adds_nothing()
    {
        _shell.OpenAnswers.Enqueue(null);
        _vm.AddElement("image");
        Assert.Empty(_vm.Design.Elements);
        Assert.False(_vm.IsDirty);
    }

    [Fact]
    public void An_unusable_image_shows_an_error_and_adds_nothing()
    {
        var path = _shell.Path_("bad.png");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6]);
        _shell.OpenAnswers.Enqueue(path);

        _vm.AddElement("image");

        Assert.Empty(_vm.Design.Elements);
        Assert.StartsWith("Error", Assert.Single(_shell.Errors).Title);
    }

    [Fact]
    public void Deleting_removes_the_selection()
    {
        _vm.AddElement("text");
        _vm.DeleteSelected();

        Assert.Empty(_vm.Design.Elements);
        Assert.Null(_vm.SelectedElement);
        Assert.False(Can(_vm.DeleteCommand));
    }

    [Fact]
    public void Duplicating_copies_the_element_with_a_new_id_and_an_offset()
    {
        _vm.AddElement("text");
        var original = (TextElement)_vm.SelectedElement!;
        original.Text = "Hello";

        _vm.DuplicateSelected();

        Assert.Equal(2, _vm.Design.Elements.Count);
        var copy = Assert.IsType<TextElement>(_vm.SelectedElement);
        Assert.NotSame(original, copy);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Hello", copy.Text);
        Assert.Equal(original.X + 5, copy.X);
    }

    [Fact]
    public void Z_order_commands_move_the_selected_element()
    {
        _vm.AddElement("rectangle");
        _vm.AddElement("text");
        _vm.AddElement("line");
        var text = _vm.Design.Elements[1];
        _vm.Select(text);

        _vm.BringToFrontCommand.Execute(null);
        Assert.Same(text, _vm.Design.Elements[^1]);

        _vm.SendToBackCommand.Execute(null);
        Assert.Same(text, _vm.Design.Elements[0]);
    }

    [Fact]
    public void Selection_dependent_commands_follow_the_selection()
    {
        Assert.False(Can(_vm.DuplicateCommand));
        Assert.False(Can(_vm.EditColumnsCommand));
        _vm.AddElement("table");
        Assert.True(Can(_vm.DuplicateCommand));
        Assert.True(Can(_vm.EditColumnsCommand));
        _vm.AddElement("text");
        Assert.False(Can(_vm.EditColumnsCommand));
    }

    [Fact]
    public void Selection_changes_raise_events_and_update_the_status_text()
    {
        int selection = 0;
        _vm.SelectionChanged += (_, _) => selection++;
        _vm.AddElement("text");
        Assert.True(selection >= 1);
        Assert.Contains("mm", _vm.SelectionText);
        _vm.Select(null);
        Assert.Equal("Nothing selected", _vm.SelectionText);
    }

    // ---------- undo / redo ----------

    [Fact]
    public void Undo_and_redo_walk_through_the_changes()
    {
        Assert.False(Can(_vm.UndoCommand));
        _vm.AddElement("text");
        _vm.AddElement("line");
        Assert.True(Can(_vm.UndoCommand));

        _vm.Undo();
        Assert.Single(_vm.Design.Elements);
        _vm.Undo();
        Assert.Empty(_vm.Design.Elements);
        Assert.False(Can(_vm.UndoCommand));
        Assert.True(Can(_vm.RedoCommand));

        _vm.Redo();
        _vm.Redo();
        Assert.Equal(2, _vm.Design.Elements.Count);
        Assert.False(Can(_vm.RedoCommand));
    }

    [Fact]
    public void A_new_change_clears_the_redo_stack()
    {
        _vm.AddElement("text");
        _vm.Undo();
        _vm.AddElement("line");
        Assert.False(Can(_vm.RedoCommand));
    }

    [Fact]
    public void Undo_keeps_the_same_element_selected_by_id()
    {
        _vm.AddElement("text");
        var id = _vm.SelectedElement!.Id;
        _vm.Nudge(5, 0);
        _vm.Undo();

        Assert.Equal(id, _vm.SelectedElement!.Id);
        Assert.Equal(10, _vm.SelectedElement.X);
    }

    // ---------- dragging ----------

    [Fact]
    public void A_drag_is_a_single_undo_step()
    {
        _vm.AddElement("rectangle");
        var rect = _vm.SelectedElement!;

        _vm.BeginEdit();
        _vm.ApplyGeometry(rect, 20, 20, 40, 20);
        _vm.ApplyGeometry(rect, 25, 30, 40, 20);
        _vm.ApplyGeometry(rect, 30, 40, 50, 25);
        _vm.EndEdit();

        Assert.Equal((30.0, 40.0, 50.0, 25.0), (rect.X, rect.Y, rect.Width, rect.Height));
        _vm.Undo();
        var restored = _vm.Design.Elements[0];
        Assert.Equal((10.0, 10.0, 40.0, 20.0), (restored.X, restored.Y, restored.Width, restored.Height));
        _vm.Undo(); // the add itself
        Assert.Empty(_vm.Design.Elements);
    }

    [Fact]
    public void A_click_without_movement_adds_no_undo_step_and_does_not_dirty_the_file()
    {
        var path = _shell.Path_("a.art");
        _vm.AddElement("rectangle");
        _shell.SaveAnswers.Enqueue(path);
        _vm.SaveAsync(false).GetAwaiter().GetResult();
        var rect = _vm.SelectedElement!;

        _vm.BeginEdit();
        _vm.ApplyGeometry(rect, rect.X, rect.Y, rect.Width, rect.Height);
        _vm.EndEdit();

        Assert.False(_vm.IsDirty);
        _vm.Undo(); // goes straight back to before the add
        Assert.Empty(_vm.Design.Elements);
    }

    [Fact]
    public void Geometry_changes_are_ignored_outside_a_gesture()
    {
        _vm.AddElement("rectangle");
        var rect = _vm.SelectedElement!;
        _vm.ApplyGeometry(rect, 99, 99, 1, 1);
        Assert.Equal(10, rect.X);
    }

    [Fact]
    public void Nudge_moves_the_selection_as_one_undoable_step()
    {
        _vm.AddElement("rectangle");
        _vm.Nudge(1, -2);
        Assert.Equal((11.0, 8.0), (_vm.SelectedElement!.X, _vm.SelectedElement.Y));
        _vm.Undo();
        Assert.Equal((10.0, 10.0), (_vm.SelectedElement!.X, _vm.SelectedElement.Y));
    }

    // ---------- zoom ----------

    [Fact]
    public void Zoom_is_clamped_and_reported_in_percent()
    {
        _vm.SetZoom(100);
        Assert.Equal(DesignerViewModel.MaxZoom, _vm.Zoom);
        _vm.SetZoom(0.01);
        Assert.Equal(DesignerViewModel.MinZoom, _vm.Zoom);
        Assert.Equal("25 %", _vm.ZoomText);
        _vm.ZoomResetCommand.Execute(null);
        Assert.Equal("100 %", _vm.ZoomText);
        _vm.ZoomInCommand.Execute(null);
        Assert.Equal(1.25, _vm.Zoom);
        _vm.ZoomOutCommand.Execute(null);
        Assert.Equal(1, _vm.Zoom);
    }

    [Fact]
    public void Changing_the_zoom_requests_a_redraw()
    {
        int changed = 0;
        _vm.Changed += (_, _) => changed++;
        _vm.SetZoom(2);
        Assert.Equal(1, changed);
    }

    // ---------- files ----------

    [Fact]
    public async Task Save_as_writes_an_art_file_and_clears_the_dirty_flag()
    {
        _vm.AddElement("text");
        _shell.SaveAnswers.Enqueue(_shell.Path_("report")); // no extension typed

        Assert.True(await _vm.SaveAsync(false));

        var path = _shell.Path_("report.art");
        Assert.True(File.Exists(path));
        Assert.Equal(path, _vm.FilePath);
        Assert.False(_vm.IsDirty);
        Assert.Equal("report — A-Report Designer", _vm.WindowTitle);
        Assert.Equal("Saved report.art", _vm.Status);
        Assert.Single((await new ArtSerializer().LoadFromFileAsync(path)).Elements);
    }

    [Fact]
    public async Task Save_overwrites_the_current_file_without_asking_again()
    {
        _shell.SaveAnswers.Enqueue(_shell.Path_("r.art"));
        await _vm.SaveAsync(false);
        _vm.AddElement("line");

        Assert.True(await _vm.SaveAsync(false));
        Assert.Single((await new ArtSerializer().LoadFromFileAsync(_shell.Path_("r.art"))).Elements);
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_saves_nothing()
    {
        _vm.AddElement("text");
        _shell.SaveAnswers.Enqueue(null);
        Assert.False(await _vm.SaveAsync(false));
        Assert.True(_vm.IsDirty);
        Assert.Null(_vm.FilePath);
    }

    [Fact]
    public async Task Save_failures_are_reported_not_thrown()
    {
        _shell.SaveAnswers.Enqueue(Path.Combine(_shell.Directory, "missing-folder", "x.art"));
        Assert.False(await _vm.SaveAsync(false));
        Assert.StartsWith("Error", Assert.Single(_shell.Errors).Title);
    }

    [Fact]
    public async Task Open_loads_a_file_and_resets_history_and_selection()
    {
        var path = _shell.Path_("in.art");
        await new ArtSerializer().SaveToFileAsync(new ReportDesign { Name = "In", Elements = [new TextElement { Text = "Hi" }] }, path);
        _vm.AddElement("line");
        _shell.OpenAnswers.Enqueue(path);

        _vm.Open();

        Assert.Equal("In", _vm.Design.Name);
        Assert.Equal(path, _vm.FilePath);
        Assert.False(_vm.IsDirty);
        Assert.Null(_vm.SelectedElement);
        Assert.False(Can(_vm.UndoCommand));
        Assert.Equal(1, _shell.ConfirmCalls); // there were unsaved changes
    }

    [Fact]
    public void Opening_an_invalid_file_shows_a_helpful_error_and_keeps_the_current_design()
    {
        var path = _shell.Path_("bad.art");
        File.WriteAllText(path, "not json");
        _vm.AddElement("text");
        _shell.OpenAnswers.Enqueue(path);

        _vm.Open();

        var error = Assert.Single(_shell.Errors);
        Assert.Contains("Open a file saved by A-Report", error.Message);
        Assert.Single(_vm.Design.Elements);
    }

    [Fact]
    public void Opening_a_missing_file_shows_an_error() 
    {
        _vm.OpenFile(_shell.Path_("nope.art"));
        Assert.Single(_shell.Errors);
    }

    [Fact]
    public void Declining_to_discard_changes_cancels_new_and_open()
    {
        _vm.AddElement("text");
        _shell.ConfirmDiscardAnswer = false;
        _shell.OpenAnswers.Enqueue(_shell.Path_("x.art"));

        _vm.New();
        _vm.Open();

        Assert.Single(_vm.Design.Elements);
        Assert.Equal(2, _shell.ConfirmCalls);
        Assert.Single(_shell.OpenAnswers); // the picker was never shown
        Assert.False(_vm.CanClose());
    }

    [Fact]
    public void New_starts_over_when_there_is_nothing_to_lose()
    {
        _vm.New();
        Assert.Equal(0, _shell.ConfirmCalls);
        Assert.True(_vm.CanClose());
    }

    // ---------- preview and sample data ----------

    [Fact]
    public async Task Preview_renders_a_pdf_and_opens_it_externally()
    {
        _vm.AddElement("text");
        ((TextElement)_vm.SelectedElement!).Text = "Hello {Name}";

        var path = await _vm.PreviewAsync();

        Assert.NotNull(path);
        Assert.Equal(path, Assert.Single(_shell.OpenedExternally));
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path!), 0, 5));
        Assert.StartsWith("Preview opened", _vm.Status);
        File.Delete(path!);
    }

    [Fact]
    public async Task Preview_reports_design_problems_instead_of_crashing()
    {
        _vm.AddElement("table");
        ((TableElement)_vm.SelectedElement!).Columns.Clear();

        Assert.Null(await _vm.PreviewAsync());
        Assert.Contains("at least one column", Assert.Single(_shell.Errors).Message);
        Assert.Empty(_shell.OpenedExternally);
    }

    [Fact]
    public async Task Preview_uses_loaded_sample_data_and_reports_warnings()
    {
        var json = _shell.Path_("data.json");
        File.WriteAllText(json, "{\"variables\":{\"Name\":\"Ada\"}}");
        _shell.OpenAnswers.Enqueue(json);
        _vm.LoadSampleData();
        Assert.True(_vm.HasSampleData);

        _vm.AddElement("table"); // data source "Items" is missing from the sample data -> a warning
        var path = await _vm.PreviewAsync();

        Assert.Contains("warning", _vm.Status);
        File.Delete(path!);
    }

    [Fact]
    public void Invalid_sample_data_shows_an_error()
    {
        var json = _shell.Path_("data.json");
        File.WriteAllText(json, "{ nope");
        _shell.OpenAnswers.Enqueue(json);
        _vm.LoadSampleData();
        Assert.False(_vm.HasSampleData);
        Assert.Single(_shell.Errors);
    }

    // ---------- columns ----------

    [Fact]
    public void Editing_columns_replaces_them_as_one_undoable_step()
    {
        _vm.AddElement("table");
        var table = (TableElement)_vm.SelectedElement!;
        _shell.ColumnEditor = _ => [new TableColumn { Header = "Only", Binding = "X" }];

        _vm.EditColumnsCommand.Execute(null);
        Assert.Equal("Only", Assert.Single(table.Columns).Header);

        _vm.Undo();
        var restored = (TableElement)_vm.SelectedElement!;
        Assert.Equal(2, restored.Columns.Count);
    }

    [Fact]
    public void Cancelling_the_column_editor_changes_nothing()
    {
        _vm.AddElement("table");
        var table = (TableElement)_vm.SelectedElement!;
        _vm.EditColumnsCommand.Execute(null);
        Assert.Equal(2, table.Columns.Count);
    }
}
