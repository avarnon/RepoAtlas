namespace RepoAtlas.McpServer.Models;

/// <summary>
/// A curated, directed dependency link from one repo to another.
/// </summary>
public sealed class DependencyLink
{
    /// <summary>
    /// The id of the repo depended upon.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// An optional note describing the dependency.
    /// </summary>
    public string? Note { get; set; }
}
