using System.Data;
using AReport.Core.Tests.Support;
using AReport.Data;
using AReport.Model;

namespace AReport.Core.Tests;

public class ValueResolverTests
{
    private sealed record Address(string City);

    private sealed record Customer(string Name, Address? Address, decimal Balance);

    private readonly ValueResolver _resolver = new();

    private object? Get(object? root, string path)
    {
        Assert.True(_resolver.TryResolve(root, path, out var value), $"'{path}' should resolve");
        return value;
    }

    [Fact]
    public void Reads_properties_ignoring_case() =>
        Assert.Equal("Ada", Get(new Customer("Ada", null, 1), "name"));

    [Fact]
    public void Reads_nested_paths() =>
        Assert.Equal("Tehran", Get(new Customer("Ada", new Address("Tehran"), 1), "Address.City"));

    [Fact]
    public void A_null_on_the_path_resolves_to_null_without_failing() =>
        Assert.Null(Get(new Customer("Ada", null, 1), "Address.City"));

    [Fact]
    public void Missing_members_do_not_resolve()
    {
        Assert.False(_resolver.TryResolve(new Customer("Ada", null, 1), "Nope", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Reads_generic_dictionaries_ignoring_case() =>
        Assert.Equal(5, Get(new Dictionary<string, object?> { ["Qty"] = 5 }, "qty"));

    [Fact]
    public void Reads_dictionaries_of_value_types() =>
        Assert.Equal(7, Get(new Dictionary<string, int> { ["Total"] = 7 }, "TOTAL"));

    [Fact]
    public void Reads_nested_dictionaries() =>
        Assert.Equal("x", Get(new Dictionary<string, object?> { ["a"] = new Dictionary<string, object?> { ["b"] = "x" } }, "a.b"));

    [Fact]
    public void Reads_data_rows_and_maps_DBNull_to_null()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Note", typeof(string));
        var row = table.Rows.Add("Ada", DBNull.Value);

        Assert.Equal("Ada", Get(row, "name"));
        Assert.Null(Get(row, "Note"));
        Assert.False(_resolver.TryResolve(row, "Missing", out _));
    }

    [Fact]
    public void Reads_data_row_views()
    {
        var table = new DataTable();
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add("Ada");
        Assert.Equal("Ada", Get(new DataView(table)[0], "Name"));
    }

    [Fact]
    public void An_empty_path_returns_the_root() => Assert.Equal("x", Get("x", ""));

    [Fact]
    public void Indexer_properties_are_not_members() =>
        Assert.False(_resolver.TryResolve(new List<int> { 1 }, "Item", out _));
}

public class ValueFormatterTests
{
    private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;

    [Fact]
    public void Null_is_empty() => Assert.Equal("", ValueFormatter.Format(null, "N2", Invariant));

    [Fact]
    public void Applies_format_strings() => Assert.Equal("1,234.50", ValueFormatter.Format(1234.5m, "N2", Invariant));

    [Fact]
    public void Uses_the_given_culture() =>
        Assert.Equal("1234,5", ValueFormatter.Format(1234.5m, null, CommaDecimalCulture()));

    private static System.Globalization.CultureInfo CommaDecimalCulture()
    {
        // Built from the invariant culture so the test does not depend on ICU/OS culture data.
        var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return culture;
    }

    [Fact]
    public void A_bad_format_string_falls_back_to_the_default() =>
        Assert.Equal("12", ValueFormatter.Format(12, "Q", Invariant));

    [Fact]
    public void Formats_dates() =>
        Assert.Equal("2026-11-20", ValueFormatter.Format(new DateTime(2026, 11, 20), "yyyy-MM-dd", Invariant));

    [Fact]
    public void Plain_objects_use_ToString() => Assert.Equal("abc", ValueFormatter.Format("abc", "N2", Invariant));
}

public class ReportDataTests
{
    [Fact]
    public void Names_are_case_insensitive()
    {
        var data = new ReportData().AddVariable("Title", "x").AddDataSource("Items", new[] { 1, 2 });
        Assert.True(data.TryGetVariable("TITLE", out var v));
        Assert.Equal("x", v);
        Assert.True(data.TryGetDataSource("items", out var rows));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Rejects_blank_names()
    {
        Assert.Throws<ArgumentException>(() => new ReportData().AddVariable(" ", 1));
        Assert.Throws<ArgumentException>(() => new ReportData().AddDataSource("", new[] { 1 }));
    }

    [Fact]
    public void A_data_table_becomes_its_rows()
    {
        var table = new DataTable();
        table.Columns.Add("A", typeof(int));
        table.Rows.Add(1);
        table.Rows.Add(2);
        var data = new ReportData().AddDataSource("T", table);
        data.TryGetDataSource("T", out var rows);
        Assert.Equal(2, rows.Count);
        Assert.IsType<DataRow>(rows[0]);
    }

    [Fact]
    public void A_dictionary_is_one_row_not_many()
    {
        var data = new ReportData().AddDataSource("One", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });
        data.TryGetDataSource("One", out var rows);
        Assert.Single(rows);
    }

    [Fact]
    public void A_single_object_is_one_row()
    {
        var data = new ReportData().AddDataSource("One", new { A = 1 });
        data.TryGetDataSource("One", out var rows);
        Assert.Single(rows);
    }

    [Fact]
    public void Null_is_an_empty_source()
    {
        var data = new ReportData().AddDataSource("None", null);
        Assert.True(data.TryGetDataSource("None", out var rows));
        Assert.Empty(rows);
    }

    [Fact]
    public void A_string_is_not_a_data_source() =>
        Assert.Throws<ArgumentException>(() => new ReportData().AddDataSource("S", "abc"));

    [Fact]
    public void Sources_are_snapshotted()
    {
        var list = new List<int> { 1 };
        var data = new ReportData().AddDataSource("L", list);
        list.Add(2);
        data.TryGetDataSource("L", out var rows);
        Assert.Single(rows);
    }

    [Fact]
    public void Images_are_registered_and_errors_remembered()
    {
        var data = new ReportData()
            .AddImage("ok", TestImages.Png(1, 1, 2, (_, _) => [1, 2, 3]))
            .AddImage("bad", [1, 2, 3, 4, 5]);

        Assert.True(data.TryGetImage("OK", out var image));
        Assert.Equal(1, image.WidthPx);
        Assert.False(data.TryGetImage("bad", out _));
        Assert.True(data.TryGetImageError("bad", out var error));
        Assert.Contains("Unsupported image format", error);
    }

    [Fact]
    public void Replacing_an_image_clears_its_previous_error()
    {
        var data = new ReportData().AddImage("a", [0, 0, 0, 0]);
        data.AddImage("a", TestImages.Jpeg(2, 2));
        Assert.True(data.TryGetImage("a", out _));
        Assert.False(data.TryGetImageError("a", out _));
    }

    [Fact]
    public void FromLegacy_maps_the_old_exporter_shapes()
    {
        var dir = TestKit.TempDirectory();
        try
        {
            var logo = Path.Combine(dir, "logo.png");
            File.WriteAllBytes(logo, TestImages.Png(2, 2, 2, (_, _) => [9, 9, 9]));

            var data = ReportData.FromLegacy(
                [new KeyValuePair<string, object>("Items", new[] { 1, 2, 3 })],
                [new KeyValuePair<string, string>("logo", logo), new KeyValuePair<string, string>("gone", Path.Combine(dir, "missing.png"))],
                [new KeyValuePair<string, object>("Title", "Q3")]);

            Assert.True(data.TryGetVariable("Title", out var title));
            Assert.Equal("Q3", title);
            Assert.True(data.TryGetDataSource("Items", out var rows));
            Assert.Equal(3, rows.Count);
            Assert.True(data.TryGetImage("logo", out _));
            Assert.False(data.TryGetImage("gone", out _));
            Assert.False(data.TryGetImageError("gone", out _));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FromLegacy_accepts_nulls()
    {
        var data = ReportData.FromLegacy(null, null, null);
        Assert.Empty(data.VariableNames);
        Assert.Empty(data.DataSourceNames);
    }

    [Fact]
    public void FromJson_builds_variables_and_sources_with_typed_values()
    {
        const string json = """
        { "variables": { "Title": "Q3", "Total": 12.5, "Count": 3, "Paid": true, "Customer": { "Name": "Ada" }, "Missing": null },
          "dataSources": { "Items": [ { "Name": "A", "Qty": 2 }, { "Name": "B", "Qty": 4 } ] } }
        """;
        var data = ReportData.FromJson(json);
        var resolver = new ValueResolver();

        data.TryGetVariable("Total", out var total);
        Assert.Equal(12.5m, total);
        data.TryGetVariable("Count", out var count);
        Assert.Equal(3L, count);
        data.TryGetVariable("Paid", out var paid);
        Assert.Equal(true, paid);
        data.TryGetVariable("Customer", out var customer);
        Assert.True(resolver.TryResolve(customer, "name", out var name));
        Assert.Equal("Ada", name);
        data.TryGetVariable("Missing", out var missing);
        Assert.Null(missing);

        data.TryGetDataSource("Items", out var rows);
        Assert.Equal(2, rows.Count);
        Assert.True(resolver.TryResolve(rows[1], "Qty", out var qty));
        Assert.Equal(4L, qty);
    }

    [Fact]
    public void FromJson_reports_invalid_json_as_a_ReportException()
    {
        Assert.Throws<ReportException>(() => ReportData.FromJson("{ nope"));
        Assert.Throws<ReportException>(() => ReportData.FromJson("[1]"));
    }
}

public class SampleDataTests
{
    [Fact]
    public void Creates_variables_for_placeholders_including_nested_paths()
    {
        var design = TestKit.Design(TestKit.Text("Hi {Name} from {Customer.Address.City} p{PageNumber}/{PageCount} {Now:yyyy}"));
        var data = SampleData.CreateFor(design);
        var resolver = new ValueResolver();

        data.TryGetVariable("Name", out var name);
        Assert.Equal("‹Name›", name);
        data.TryGetVariable("Customer", out var customer);
        Assert.True(resolver.TryResolve(customer, "Address.City", out var city));
        Assert.Equal("‹Customer.Address.City›", city);
        Assert.False(data.TryGetVariable("PageNumber", out _));
        Assert.False(data.TryGetVariable("Now", out _));
    }

    [Fact]
    public void Creates_rows_for_table_data_sources()
    {
        var design = TestKit.Design(TestKit.Table("Items", 10, ("Name", "Name"), ("No", "#"), ("City", "Address.City")));
        var data = SampleData.CreateFor(design);

        Assert.True(data.TryGetDataSource("Items", out var rows));
        Assert.Equal(12, rows.Count);
        var resolver = new ValueResolver();
        Assert.True(resolver.TryResolve(rows[2], "Name", out var name));
        Assert.Equal("Name 3", name);
        Assert.True(resolver.TryResolve(rows[0], "Address.City", out var city));
        Assert.Equal("Address.City 1", city);
    }

    [Fact]
    public void Sample_data_renders_without_warnings()
    {
        var design = TestKit.Design(TestKit.Text("Hello {Name}"), TestKit.Table("Items", 30, ("Name", "Name")));
        var doc = TestKit.Layout().Layout(design, SampleData.CreateFor(design));
        Assert.Empty(doc.Warnings);
    }
}
