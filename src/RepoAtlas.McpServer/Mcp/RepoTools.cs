using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace RepoAtlas.McpServer.Mcp;

/// <summary>
/// MCP tools for curating the repo catalog: description, tags, dependency links, and rescanning.
/// Each method's <see cref="IRepoCatalog"/> and <c>ILogger&lt;RepoTools&gt;</c> parameters are
/// resolved from DI by the MCP SDK; the remaining parameters come from the tool call's arguments.
/// </summary>
[McpServerToolType]
public sealed class RepoTools
{
    /// <summary>
    /// MCP tool: replaces the stored description for a repo.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="id">The repo's id, e.g. 'owner/repo'.</param>
    /// <param name="description">The new description text.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Replaces the stored description for a repo.")]
    public static async Task<string> UpdateDescription(
        IRepoCatalog catalog,
        ILogger<RepoTools> logger,
        [Description("The repo's id, e.g. 'owner/repo'.")] string id,
        [Description("The new description text.")] string description,
        CancellationToken cancellationToken = default)
    {
        await RunAsync("UpdateDescription", logger, ct => catalog.UpdateDescriptionAsync(id, description, ct), cancellationToken);
        return $"Updated description for '{id}'.";
    }

    /// <summary>
    /// MCP tool: adds a tag to a repo if not already present.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="id">The repo's id.</param>
    /// <param name="tag">The tag to add.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Adds a tag to a repo if not already present.")]
    public static async Task<string> AddTag(
        IRepoCatalog catalog,
        ILogger<RepoTools> logger,
        [Description("The repo's id.")] string id,
        [Description("The tag to add.")] string tag,
        CancellationToken cancellationToken = default)
    {
        await RunAsync("AddTag", logger, ct => catalog.AddTagAsync(id, tag, ct), cancellationToken);
        return $"Tag '{tag}' added to '{id}'.";
    }

    /// <summary>
    /// MCP tool: removes a tag from a repo if present.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="id">The repo's id.</param>
    /// <param name="tag">The tag to remove.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Removes a tag from a repo if present.")]
    public static async Task<string> RemoveTag(
        IRepoCatalog catalog,
        ILogger<RepoTools> logger,
        [Description("The repo's id.")] string id,
        [Description("The tag to remove.")] string tag,
        CancellationToken cancellationToken = default)
    {
        await RunAsync("RemoveTag", logger, ct => catalog.RemoveTagAsync(id, tag, ct), cancellationToken);
        return $"Tag '{tag}' removed from '{id}'.";
    }

    /// <summary>
    /// MCP tool: adds a directed dependency link, recording that <c>fromId</c>'s code depends on <c>toId</c>.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="fromId">The dependent repo's id.</param>
    /// <param name="toId">The depended-upon repo's id.</param>
    /// <param name="note">Optional note about the dependency.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Adds a directed dependency link: fromId's code depends on toId.")]
    public static async Task<string> AddDependency(
        IRepoCatalog catalog,
        ILogger<RepoTools> logger,
        [Description("The dependent repo's id.")] string fromId,
        [Description("The depended-upon repo's id.")] string toId,
        [Description("Optional note about the dependency.")] string? note = null,
        CancellationToken cancellationToken = default)
    {
        await RunAsync("AddDependency", logger, ct => catalog.AddDependencyAsync(fromId, toId, note, ct), cancellationToken);
        return $"Dependency added: '{fromId}' depends on '{toId}'.";
    }

    /// <summary>
    /// MCP tool: removes a dependency link between two repos.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="fromId">The dependent repo's id.</param>
    /// <param name="toId">The depended-upon repo's id.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Removes a dependency link between two repos.")]
    public static async Task<string> RemoveDependency(
        IRepoCatalog catalog,
        ILogger<RepoTools> logger,
        [Description("The dependent repo's id.")] string fromId,
        [Description("The depended-upon repo's id.")] string toId,
        CancellationToken cancellationToken = default)
    {
        await RunAsync("RemoveDependency", logger, ct => catalog.RemoveDependencyAsync(fromId, toId, ct), cancellationToken);
        return $"Dependency removed: '{fromId}' no longer depends on '{toId}'.";
    }

    /// <summary>
    /// MCP tool: re-scans the configured root directories for git repositories, refreshing paths and reachability.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Re-scans the configured root directories for git repositories, refreshing paths and reachability.")]
    public static async Task<string> Rescan(IRepoCatalog catalog, ILogger<RepoTools> logger, CancellationToken cancellationToken = default)
    {
        var summary = await RunAsync("Rescan", logger, catalog.RescanAsync, cancellationToken);
        return JsonSerializer.Serialize(summary);
    }

    /// <summary>
    /// MCP tool: adds a root directory to scan for git repositories, if not already configured.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="root">The root directory to add.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Adds a root directory to scan for git repositories, if not already configured. Call Rescan afterward to discover repos under it.")]
    public static async Task<string> AddRoot(
        IRepoCatalog catalog,
        ILogger<RepoTools> logger,
        [Description("The root directory to add, e.g. 'C:\\code' or '/home/me/code'.")] string root,
        CancellationToken cancellationToken = default)
    {
        await RunAsync("AddRoot", logger, ct => catalog.AddRootAsync(root, ct), cancellationToken);
        return $"Root '{root}' added.";
    }

    /// <summary>
    /// MCP tool: removes a configured root directory.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="root">The root directory to remove.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Removes a configured root directory. Does not remove repos already in the catalog; call Rescan afterward to flag repos that are no longer reachable.")]
    public static async Task<string> RemoveRoot(
        IRepoCatalog catalog,
        ILogger<RepoTools> logger,
        [Description("The root directory to remove.")] string root,
        CancellationToken cancellationToken = default)
    {
        await RunAsync("RemoveRoot", logger, ct => catalog.RemoveRootAsync(root, ct), cancellationToken);
        return $"Root '{root}' removed.";
    }

    /// <summary>
    /// MCP tool: lists the configured root directories scanned for git repositories.
    /// </summary>
    /// <param name="catalog">Resolved from DI.</param>
    /// <param name="logger">Resolved from DI.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    [McpServerTool, Description("Lists the configured root directories scanned for git repositories.")]
    public static async Task<string> ListRoots(IRepoCatalog catalog, ILogger<RepoTools> logger, CancellationToken cancellationToken = default)
    {
        var roots = await RunAsync("ListRoots", logger, catalog.ListRootsAsync, cancellationToken);
        return JsonSerializer.Serialize(roots);
    }

    /// <summary>
    /// Runs a catalog mutation with tracing/metrics/exception-translation, discarding its result.
    /// </summary>
    /// <param name="toolName">The tool's name, used to tag the span and the invocation metric.</param>
    /// <param name="logger">Logger for translated (client-facing) failures.</param>
    /// <param name="action">The catalog operation to run.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    private static async Task RunAsync(
        string toolName,
        ILogger<RepoTools> logger,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await RunAsync(toolName, logger, async ct =>
        {
            await action(ct);
            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Runs a catalog operation with tracing/metrics/exception-translation, returning its result.
    /// Domain exceptions (<see cref="RepoNotFoundException"/>, <see cref="InvalidDependencyException"/>)
    /// are logged and translated into client-facing <see cref="McpException"/>s.
    /// </summary>
    /// <typeparam name="T">The type of value the operation returns.</typeparam>
    /// <param name="toolName">The tool's name, used to tag the span and the invocation metric.</param>
    /// <param name="logger">Logger for translated (client-facing) failures.</param>
    /// <param name="action">The catalog operation to run.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The operation's result.</returns>
    private static async Task<T> RunAsync<T>(
        string toolName,
        ILogger<RepoTools> logger,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity($"McpTool.{toolName}");
        var outcome = "success";
        try
        {
            return await action(cancellationToken);
        }
        catch (RepoNotFoundException ex)
        {
            outcome = "not_found";
            logger.LogWarning(ex, "Tool {ToolName} failed: {Message}", toolName, ex.Message);
            throw new McpException(ex.Message);
        }
        catch (InvalidDependencyException ex)
        {
            outcome = "invalid_dependency";
            logger.LogWarning(ex, "Tool {ToolName} failed: {Message}", toolName, ex.Message);
            throw new McpException(ex.Message);
        }
        catch (RootNotAllowedException ex)
        {
            outcome = "root_not_allowed";
            logger.LogWarning(ex, "Tool {ToolName} failed: {Message}", toolName, ex.Message);
            throw new McpException(ex.Message);
        }
        catch (Exception)
        {
            outcome = "error";
            throw;
        }
        finally
        {
            RepoAtlasTelemetry.ToolInvocationCount.Add(1,
                new KeyValuePair<string, object?>("tool", toolName),
                new KeyValuePair<string, object?>("outcome", outcome));
        }
    }
}
