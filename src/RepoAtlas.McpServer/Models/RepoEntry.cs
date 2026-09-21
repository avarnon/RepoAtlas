namespace RepoAtlas.McpServer.Models;

/// <summary>
/// A single repo's curated, persisted entry in the data store.
/// </summary>
public sealed class RepoEntry
{
    /// <summary>
    /// The absolute path to the repo's folder on disk, as of the last successful scan.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The curated description text.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// The curated tags.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// The repo's curated outgoing dependency links.
    /// </summary>
    public List<DependencyLink> DependsOn { get; set; } = new();

    /// <summary>
    /// Whether the repo's folder was not found on disk during the last rescan.
    /// </summary>
    public bool Unreachable { get; set; }
}
