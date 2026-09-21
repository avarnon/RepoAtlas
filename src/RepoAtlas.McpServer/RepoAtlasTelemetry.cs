using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RepoAtlas.McpServer;

/// <summary>
/// Shared OpenTelemetry instruments for the RepoAtlas MCP server. All tracing spans and metrics
/// in the app are created from the <see cref="ActivitySource"/> and <see cref="Meter"/> defined
/// here, and registered under <see cref="ServiceName"/> in <c>Program.cs</c>.
/// </summary>
public static class RepoAtlasTelemetry
{
    /// <summary>
    /// The OpenTelemetry resource/service name this app's telemetry is reported under, and the
    /// name registered with <c>AddSource</c>/<c>AddMeter</c> when wiring up the OTel providers.
    /// </summary>
    public const string ServiceName = "RepoAtlas.McpServer";

    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> used to start tracing spans throughout the app.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    /// <summary>
    /// The <see cref="System.Diagnostics.Metrics.Meter"/> used to create metric instruments throughout the app.
    /// </summary>
    public static readonly Meter Meter = new(ServiceName);

    /// <summary>
    /// Counts MCP tool and resource invocations, tagged by tool name and outcome (success/error/etc.).
    /// </summary>
    public static readonly Counter<long> ToolInvocationCount = Meter.CreateCounter<long>(
        "repoatlas.mcp.tool_invocations",
        description: "Number of MCP tool and resource invocations, by name and outcome.");

    /// <summary>
    /// Records the duration, in milliseconds, of each <c>RepoCatalog.RescanAsync</c> call.
    /// </summary>
    public static readonly Histogram<double> RescanDurationMs = Meter.CreateHistogram<double>(
        "repoatlas.rescan.duration",
        unit: "ms",
        description: "Duration of RepoCatalog.RescanAsync calls.");
}
