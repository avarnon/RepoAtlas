namespace RepoAtlas.McpServer.Discovery;

/// <summary>
/// A git repository found on disk during a scan.
/// </summary>
/// <param name="Id">The repo's derived "owner/repo" (or folder-name fallback) id.</param>
/// <param name="Path">The absolute path to the repo's root directory.</param>
public sealed record DiscoveredRepo(string Id, string Path);
