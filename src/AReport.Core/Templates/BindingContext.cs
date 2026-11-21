using System.Collections.ObjectModel;
using System.Globalization;
using AReport.Data;

namespace AReport.Templates;

/// <summary>What a placeholder can see while a page is being built.</summary>
public sealed record BindingContext(
    ReportData Data,
    int PageNumber,
    int PageCount,
    DateTimeOffset Now,
    CultureInfo Culture,
    bool Strict);

/// <summary>A warning list that ignores repeats (an element repeated on every page warns once).</summary>
public sealed class DistinctWarnings : Collection<string>
{
    protected override void InsertItem(int index, string item)
    {
        if (!Contains(item)) base.InsertItem(index, item);
    }
}
