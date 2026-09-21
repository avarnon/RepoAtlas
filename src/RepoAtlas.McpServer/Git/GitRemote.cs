namespace RepoAtlas.McpServer.Git;

/// <summary>
/// A named git remote and its URL, with any embedded credentials stripped.
/// </summary>
/// <param name="Name">The remote's name, e.g. "origin" or "upstream".</param>
/// <param name="Url">The remote's URL.</param>
public sealed record GitRemote(string Name, string Url);
