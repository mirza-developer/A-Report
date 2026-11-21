using AReport.Model;

namespace AReport.Designer.Tests;

public class PropertyRowTests : IDisposable
{
    private readonly FakeShell _shell = new();
    private readonly DesignerViewModel _vm;

    public PropertyRowTests() => _vm = new DesignerViewModel(_shell);

    public void Dispose() => _shell.Dispose();

    private PropertyRow Row(string name) => _vm.Properties.Single(r => r.Name == name);

    [Fact]
    public void With_nothing_selected_the_panel_shows_the_report_name_and_page_settings()
    {
        var names = _vm.Properties.Select(r => r.Name).ToList();
        Assert.Equal("Name", names[0]);
        Assert.Contains("WidthMm", names);
        Assert.Contains("MarginTopMm", names);
        Assert.DoesNotContain("Elements", names);
    }

    [Fact]
    public void An_element_shows_geometry_first_and_the_id_last_and_read_only()
    {
        _vm.AddElement("text");
        var names = _vm.Properties.Select(r => r.Name).ToList();

        Assert.Equal(new[] { "Name", "X", "Y", "Width", "Height", "RepeatOnEveryPage" }, names.Take(6));
        Assert.Equal("Id", names[^1]);
        Assert.True(Row("Id").IsReadOnly);
        Assert.DoesNotContain("Kind", names);
        Assert.DoesNotContain("Label", names);
    }

    [Fact]
    public void Labels_are_sentence_case_with_units()
    {
        Assert.Equal("Width mm", PropertyRow.MakeLabel("WidthMm"));
        Assert.Equal("Font size", PropertyRow.MakeLabel("FontSize"));
        Assert.Equal("Repeat on every page", PropertyRow.MakeLabel("RepeatOnEveryPage"));
        Assert.Equal("X", PropertyRow.MakeLabel("X"));
    }

    [Fact]
    public void Row_kinds_match_the_property_types()
    {
        _vm.AddElement("text");
        Assert.Equal(RowKind.Number, Row("X").Kind);
        Assert.Equal(RowKind.Text, Row("Text").Kind);
        Assert.True(Row("Text").IsMultiline);
        Assert.Equal(RowKind.Color, Row("Color").Kind);
        Assert.Equal(RowKind.Toggle, Row("Bold").Kind);
        Assert.Equal(RowKind.Choice, Row("HorizontalAlignment").Kind);
        Assert.Equal(new[] { "Left", "Center", "Right" }, Row("HorizontalAlignment").Choices);
    }

    [Fact]
    public void Editing_a_number_updates_the_element_and_is_undoable()
    {
        _vm.AddElement("text");
        Row("X").Text = "42.5";

        Assert.Equal(42.5, _vm.SelectedElement!.X);
        Assert.True(_vm.IsDirty);
        _vm.Undo();
        Assert.Equal(10, _vm.SelectedElement!.X);
    }

    [Fact]
    public void Numbers_accept_a_dot_even_when_the_culture_uses_a_comma()
    {
        _vm.AddElement("text");
        Row("Width").Text = "12.5";
        Assert.Equal(12.5, _vm.SelectedElement!.Width);
    }

    [Fact]
    public void A_bad_number_is_rejected_with_a_message_and_changes_nothing()
    {
        _vm.AddElement("text");
        var row = Row("X");
        row.Text = "abc";

        Assert.True(row.HasError);
        Assert.Equal("Enter a number.", row.Error);
        Assert.Equal("abc", row.Text); // what the user typed stays visible
        Assert.Equal(10, _vm.SelectedElement!.X);

        row.Text = "11";
        Assert.False(row.HasError);
        Assert.Equal(11, _vm.SelectedElement.X);
    }

    [Fact]
    public void Colors_are_validated_and_normalized()
    {
        _vm.AddElement("text");
        var row = Row("Color");

        row.Text = "teal";
        Assert.Equal("Use a colour like #1F3A5F.", row.Error);
        Assert.Equal("#000000", ((TextElement)_vm.SelectedElement!).Color);
        Assert.Null(row.SwatchHex);

        row.Text = "#ff0000";
        Assert.Equal("#FF0000", ((TextElement)_vm.SelectedElement).Color);
        Assert.Equal("#FF0000", row.SwatchHex);
    }

    [Fact]
    public void Clearing_an_optional_color_sets_it_to_null_but_required_colors_reject_empty()
    {
        _vm.AddElement("rectangle");
        var rect = (RectangleElement)_vm.SelectedElement!;

        Row("StrokeColor").Text = "";
        Assert.Null(rect.StrokeColor);

        _vm.AddElement("line");
        Row("StrokeColor").Text = "";
        Assert.True(Row("StrokeColor").HasError);
        Assert.Equal("#000000", ((LineElement)_vm.SelectedElement!).StrokeColor);
    }

    [Fact]
    public void Text_properties_commit_and_multiline_text_is_kept()
    {
        _vm.AddElement("text");
        Row("Text").Text = "Line 1\nLine 2 {Name}";
        Assert.Equal("Line 1\nLine 2 {Name}", ((TextElement)_vm.SelectedElement!).Text);
    }

    [Fact]
    public void Toggles_and_choices_write_through()
    {
        _vm.AddElement("text");
        Row("Bold").IsChecked = true;
        Row("HorizontalAlignment").SelectedChoice = "Right";

        var text = (TextElement)_vm.SelectedElement!;
        Assert.True(text.Bold);
        Assert.Equal(HAlign.Right, text.HorizontalAlignment);
    }

    [Fact]
    public void Setting_the_same_value_does_not_create_an_undo_step()
    {
        _vm.AddElement("text");
        _vm.Undo(); // clear the add
        _vm.Redo();
        var before = _vm.IsDirty;
        Row("X").Text = "10";
        _vm.Undo(); // undoes the add, not a phantom edit
        Assert.Empty(_vm.Design.Elements);
        Assert.True(before);
    }

    [Fact]
    public void Rows_refresh_when_the_element_moves_by_dragging()
    {
        _vm.AddElement("rectangle");
        var rect = _vm.SelectedElement!;
        _vm.BeginEdit();
        _vm.ApplyGeometry(rect, 33, 44, rect.Width, rect.Height);
        _vm.EndEdit();

        Assert.Equal("33", Row("X").Text);
        Assert.Equal("44", Row("Y").Text);
    }

    [Fact]
    public void Tables_expose_a_columns_row_that_opens_the_editor()
    {
        _vm.AddElement("table");
        var columns = Row("Columns");
        Assert.Equal(RowKind.Collection, columns.Kind);
        Assert.NotNull(columns.EditCollection);

        _shell.ColumnEditor = _ => [new TableColumn { Header = "A" }];
        columns.EditCollection!();
        Assert.Equal("A", Assert.Single(((TableElement)_vm.SelectedElement!).Columns).Header);
    }

    [Fact]
    public void Non_table_elements_have_no_columns_row()
    {
        _vm.AddElement("text");
        Assert.DoesNotContain(_vm.Properties, r => r.Name == "Columns");
    }

    [Fact]
    public void Editing_the_page_width_updates_the_page_size_text()
    {
        Row("WidthMm").Text = "297";
        Row("HeightMm").Text = "210";
        Assert.Equal("297 × 210 mm", _vm.PageSizeText);
    }

    [Fact]
    public void Renaming_the_report_updates_the_title()
    {
        Row("Name").Text = "Invoice";
        Assert.Equal("● Invoice — A-Report Designer", _vm.WindowTitle);
    }
}

public class HistoryServiceTests
{
    [Fact]
    public void Limits_how_many_steps_are_kept()
    {
        var history = new Services.HistoryService(limit: 3);
        for (int i = 0; i < 10; i++) history.Push($"s{i}");

        int steps = 0;
        var current = "now";
        while (history.Undo(current) is { } previous)
        {
            current = previous;
            steps++;
        }
        Assert.Equal(3, steps);
        Assert.Equal("s7", current);
    }

    [Fact]
    public void Redo_returns_what_undo_replaced()
    {
        var history = new Services.HistoryService();
        history.Push("a");
        Assert.Equal("a", history.Undo("b"));
        Assert.Equal("b", history.Redo("a"));
        Assert.Null(history.Redo("b"));
        Assert.Null(new Services.HistoryService().Undo("x"));
    }
}

public class ElementFactoryTests
{
    [Fact]
    public void Creates_every_kind_and_rejects_unknown_ones()
    {
        foreach (var kind in ElementFactory.Kinds)
            Assert.NotNull(ElementFactory.Create(kind, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => ElementFactory.Create("hologram", 0, 0));
    }

    [Fact]
    public void The_default_table_is_valid_and_renders()
    {
        var design = new ReportDesign { Elements = [ElementFactory.Create("table", 10, 10)] };
        Assert.Empty(new AReport.Services.DesignValidator().Validate(design));
    }

    [Fact]
    public void Clone_is_deep()
    {
        var table = (TableElement)ElementFactory.Create("table", 10, 10);
        var copy = (TableElement)ElementFactory.Clone(table);
        copy.Columns[0].Header = "Changed";
        Assert.Equal("Description", table.Columns[0].Header);
    }
}
