namespace RepoAtlas.McpServer;

/// <summary>
/// A summary view of a cataloged repo, as returned by the repo list.
/// </summary>
/// <param name="Id">The repo's id.</param>
/// <param name="Description">The curated description text.</param>
/// <param name="Tags">The curated tags.</param>
/// <param name="Unreachable">Whether the repo's folder was not found on disk during the last rescan.</param>
public sealed record RepoSummary(string Id, string Description, IReadOnlyList<string> Tags, bool Unreachable);
