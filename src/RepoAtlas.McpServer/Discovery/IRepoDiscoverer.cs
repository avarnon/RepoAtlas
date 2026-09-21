namespace RepoAtlas.McpServer.Discovery;

/// <summary>
/// Scans directory trees for git repositories.
/// </summary>
public interface IRepoDiscoverer
{
    /// <summary>
    /// Recursively scans the given root directories for git repositories, without descending
    /// into a repository once it has been found.
    /// </summary>
    /// <param name="roots">The root directories to scan.</param>
    /// <returns>The repositories found, any missing roots, and any paths that had to be skipped.</returns>
    ScanResult Scan(IEnumerable<string> roots);
}
