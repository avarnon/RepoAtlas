namespace RepoAtlas.McpServer;

/// <summary>
/// Reads a repository's README.md content for display, if it has one.
/// </summary>
public interface IReadmeReader
{
    /// <summary>
    /// Reads the README.md content from a repository's root directory.
    /// </summary>
    /// <param name="repoPath">The path to the repository's root directory.</param>
    /// <returns>
    /// The README content, a placeholder message if it exceeds the display size limit, or
    /// <see langword="null"/> if the repo has no README.md.
    /// </returns>
    string? Read(string repoPath);
}
