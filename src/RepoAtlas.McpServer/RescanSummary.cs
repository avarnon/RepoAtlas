namespace RepoAtlas.McpServer;

/// <summary>
/// The outcome of a <see cref="RepoCatalog.RescanAsync"/> call.
/// </summary>
/// <param name="Added">Ids of repos newly added to the catalog.</param>
/// <param name="NewlyUnreachable">Ids of previously-reachable repos whose folder was not found this time.</param>
/// <param name="NewlyReachable">Ids of previously-unreachable repos whose folder was found again.</param>
/// <param name="MissingRoots">Configured root directories that did not exist on disk.</param>
/// <param name="Skipped">Descriptions of paths that looked like repos but couldn't be read, or of duplicate-id collisions.</param>
public sealed record RescanSummary(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> NewlyUnreachable,
    IReadOnlyList<string> NewlyReachable,
    IReadOnlyList<string> MissingRoots,
    IReadOnlyList<string> Skipped);
