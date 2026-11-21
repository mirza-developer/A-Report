using AReport.Data;
using AReport.Model;
using AReport.Serialization;
using AReport.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AReport.Blazor.Tests;

public class ServiceRegistrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "areport-blazor-" + Guid.NewGuid().ToString("N"));

    public ServiceRegistrationTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private ServiceProvider Build(Action<ReportOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime>(new NullJsRuntime());
        services.AddAReport(configure ?? (o => o.ReportsDirectory = _dir));
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Registers_the_report_services()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        Assert.NotNull(provider.GetRequiredService<IReportService>());
        Assert.NotNull(provider.GetRequiredService<ReportExporter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IReportBrowser>());
    }

    [Fact]
    public void The_report_service_and_exporter_are_singletons()
    {
        using var provider = Build();
        Assert.Same(provider.GetRequiredService<IReportService>(), provider.GetRequiredService<IReportService>());
        Assert.Same(provider.GetRequiredService<ReportExporter>(), provider.GetRequiredService<ReportExporter>());
    }

    [Fact]
    public void The_browser_helper_is_scoped_per_circuit()
    {
        using var provider = Build();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IReportBrowser>(), second.ServiceProvider.GetRequiredService<IReportBrowser>());
    }

    [Fact]
    public void The_configure_callback_reaches_the_options()
    {
        using var provider = Build(o => { o.ReportsDirectory = _dir; o.StrictBindings = true; });
        var options = provider.GetRequiredService<ReportOptions>();
        Assert.Equal(_dir, options.ReportsDirectory);
        Assert.True(options.StrictBindings);
    }

    [Fact]
    public async Task The_registered_exporter_renders_a_design_from_the_configured_folder()
    {
        await new ArtSerializer().SaveToFileAsync(
            new ReportDesign { Name = "Hello", Elements = [new TextElement { Text = "Hi {Who}" }] },
            Path.Combine(_dir, "hello.art"));

        using var provider = Build();
        using var stream = await provider.GetRequiredService<ReportExporter>().DownloadPdf(
            "hello", "Greeting", [], [], [new KeyValuePair<string, object>("Who", "Ada")]);

        var bytes = stream.ToArray();
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task A_custom_design_source_registered_first_is_used()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime>(new NullJsRuntime());
        services.AddSingleton<IDesignSource>(new FixedSource());
        services.AddAReport();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IReportService>().GenerateAsync("whatever", new ReportData());
        Assert.Equal("Fixed.pdf", result.FileName);
    }

    private sealed class FixedSource : IDesignSource
    {
        public Task<ReportDesign> LoadAsync(string reportName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReportDesign { Name = "Fixed" });
    }

    private sealed class NullJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }
}
