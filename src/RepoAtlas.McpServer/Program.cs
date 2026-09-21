using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RepoAtlas.McpServer;
using RepoAtlas.McpServer.Discovery;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Mcp;
using RepoAtlas.McpServer.Storage;

var dataFilePath = Environment.GetEnvironmentVariable("REPOATLAS_DATA_PATH")
    ?? Path.Combine(GetDefaultConfigDirectory(), "RepoAtlas", "data.yaml");

var allowedRootBases = AllowedRootBasesParser.Parse(Environment.GetEnvironmentVariable("REPOATLAS_ALLOWED_ROOT_BASES"));

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
});

var otel = builder.Services.AddOpenTelemetry();
otel.ConfigureResource(resource => resource.AddService(RepoAtlasTelemetry.ServiceName));
otel.WithTracing(tracing => tracing.AddSource(RepoAtlasTelemetry.ServiceName));
otel.WithMetrics(metrics => metrics.AddMeter(RepoAtlasTelemetry.ServiceName));

if (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") is not null)
{
    otel.UseOtlpExporter();
}

builder.Services.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
builder.Services.AddSingleton<IRepoDataStore>(sp => new YamlDataStore(
    dataFilePath,
    sp.GetRequiredService<IAtomicFileWriter>(),
    sp.GetRequiredService<ILogger<YamlDataStore>>()));
builder.Services.AddSingleton<IGitFactsReader, GitFactsReader>();
builder.Services.AddSingleton<IRepoDiscoverer, RepoDiscoverer>();
builder.Services.AddSingleton<IReadmeReader, ReadmeReader>();
builder.Services.AddSingleton<IRepoCatalog>(sp => new RepoCatalog(
    sp.GetRequiredService<IRepoDataStore>(),
    sp.GetRequiredService<IRepoDiscoverer>(),
    sp.GetRequiredService<IGitFactsReader>(),
    sp.GetRequiredService<IReadmeReader>(),
    sp.GetRequiredService<ILogger<RepoCatalog>>(),
    allowedRootBases));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResources<RepoResources>();

var host = builder.Build();

try
{
    await host.Services.GetRequiredService<IRepoCatalog>().RescanAsync();
}
catch (Exception ex)
{
    host.Services.GetRequiredService<ILogger<Program>>().LogCritical(ex, "RepoAtlas failed to start.");
    return 1;
}

await host.RunAsync();
return 0;

// Resolves the OS-appropriate config directory (%APPDATA% on Windows, ~/.config on Linux,
// ~/Library/Application Support on macOS) that REPOATLAS_DATA_PATH defaults under.
//
// Uses SpecialFolderOption.DoNotVerify because the default (None) behavior returns an empty
// string when the folder doesn't already exist on disk — common on Unix, where nothing creates
// ~/.config ahead of time. AtomicFileWriter creates the directory on first write, so the folder
// need not exist yet here.
static string GetDefaultConfigDirectory()
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
    if (!string.IsNullOrWhiteSpace(appData))
    {
        return appData;
    }

    throw new InvalidOperationException(
        "Could not determine a default data directory (no home directory found for the current user). " +
        "Set the REPOATLAS_DATA_PATH environment variable explicitly.");
}
