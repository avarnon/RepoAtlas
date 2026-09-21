namespace RepoAtlas.McpServer.Discovery;

/// <summary>
/// The outcome of scanning a set of root directories for git repositories.
/// </summary>
/// <param name="Repos">The repositories found.</param>
/// <param name="MissingRoots">Configured root directories that did not exist on disk.</param>
/// <param name="Skipped">Paths that looked like git repositories but could not be read.</param>
public sealed record ScanResult(IReadOnlyList<DiscoveredRepo> Repos, IReadOnlyList<string> MissingRoots, IReadOnlyList<SkippedPath> Skipped);
