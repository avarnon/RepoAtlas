namespace RepoAtlas.McpServer.Models;

/// <summary>
/// The root document persisted to the YAML data file: configured scan roots plus every known repo's curated entry.
/// </summary>
public sealed class DataStoreDocument
{
    /// <summary>
    /// The root directories scanned for git repositories.
    /// </summary>
    public List<string> Roots { get; set; } = new();

    /// <summary>
    /// Known repos, keyed by id.
    /// </summary>
    public Dictionary<string, RepoEntry> Repos { get; set; } = new();
}
