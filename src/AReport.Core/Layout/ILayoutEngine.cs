using AReport.Data;
using AReport.Model;

namespace AReport.Layout;

public interface ILayoutEngine
{
    /// <summary>Resolves bindings, paginates tables and produces renderer-independent pages.</summary>
    LaidOutDocument Layout(ReportDesign design, ReportData data, string? title = null);
}
