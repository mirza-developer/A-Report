using AReport.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AReport.Blazor;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IReportService"/>, <see cref="ReportExporter"/> (the drop-in for the old PdfExporter)
    /// and <see cref="IReportBrowser"/>. Register your own <see cref="IDesignSource"/> first to load designs from somewhere else.
    /// </summary>
    public static IServiceCollection AddAReport(this IServiceCollection services, Action<ReportOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ReportOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IReportService>(sp =>
            ReportServiceFactory.Create(sp.GetRequiredService<ReportOptions>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<IDesignSource>()));
        services.TryAddSingleton<ReportExporter>();
        services.TryAddScoped<IReportBrowser, ReportBrowser>();
        return services;
    }
}
