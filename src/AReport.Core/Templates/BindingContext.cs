using System.Collections.ObjectModel;
using System.Globalization;
using AReport.Data;

namespace AReport.Templates;

/// <summary>What a placeholder can see while a page is being built.</summary>
/// <param name="Data">Variables, data sources and images supplied by the application.</param>
/// <param name="PageNumber">1-based number of the page being built.</param>
/// <param name="PageCount">Total number of pages.</param>
/// <param name="Now">The time used for <c>{Now}</c>.</param>
/// <param name="Culture">Culture for number and date formatting.</param>
/// <param name="Strict">Throw on unresolved placeholders.</param>
/// <param name="Row">The current item inside a repeater row (null elsewhere).</param>
/// <param name="RowSource">Name of the data source the row comes from.</param>
/// <param name="Line">1-based number of the current row (0 outside a repeater).</param>
public sealed record BindingContext(
    ReportData Data,
    int PageNumber,
    int PageCount,
    DateTimeOffset Now,
    CultureInfo Culture,
    bool Strict,
    object? Row = null,
    string? RowSource = null,
    int Line = 0);

/// <summary>A warning list that ignores repeats (an element repeated on every page warns once).</summary>
public sealed class DistinctWarnings : Collection<string>
{
    protected override void InsertItem(int index, string item)
    {
        if (!Contains(item)) base.InsertItem(index, item);
    }
}
