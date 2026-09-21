using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RepoAtlas.McpServer.Discovery;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests;

public class TelemetryTests
{
    [Fact]
    public async Task RescanAsync_EmitsATraceSpanAndARescanDurationMetric_UnderTheRegisteredServiceName()
    {
        var exportedActivities = new ThreadSafeCollection<Activity>();
        var exportedMetrics = new ThreadSafeCollection<Metric>();

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(RepoAtlasTelemetry.ServiceName)
            .AddInMemoryExporter(exportedActivities)
            .Build();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(RepoAtlasTelemetry.ServiceName)
            .AddInMemoryExporter(exportedMetrics)
            .Build();

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var emptyScan = new ScanResult(Array.Empty<DiscoveredRepo>(), Array.Empty<string>(), Array.Empty<SkippedPath>());
            var catalog = CatalogTestFactory.NewCatalog(store, scan: _ => emptyScan);

            await catalog.RescanAsync();
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }

        tracerProvider.ForceFlush();
        meterProvider.ForceFlush();

        Assert.Contains(exportedActivities, a => a.DisplayName == "RepoCatalog.Rescan");
        Assert.Contains(exportedMetrics, m => m.Name == "repoatlas.rescan.duration");
    }
}
