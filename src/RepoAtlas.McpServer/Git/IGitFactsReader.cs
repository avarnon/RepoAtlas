namespace RepoAtlas.McpServer.Git;

/// <summary>
/// Reads live facts from a repository's on-disk git state.
/// </summary>
public interface IGitFactsReader
{
    /// <summary>
    /// Reads the configured remotes for a repository, with any embedded credentials stripped from their URLs.
    /// </summary>
    /// <param name="repoPath">The path to the repository's root directory.</param>
    /// <returns>The repo's remotes.</returns>
    IReadOnlyList<GitRemote> GetRemotes(string repoPath);

    /// <summary>
    /// Reads a full snapshot of live git facts for a repository: remotes, current branch, HEAD, and fork lineage.
    /// </summary>
    /// <param name="repoPath">The path to the repository's root directory.</param>
    /// <returns>The repo's live git facts.</returns>
    GitFacts Read(string repoPath);
}
