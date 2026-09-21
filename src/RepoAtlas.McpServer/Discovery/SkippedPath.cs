namespace RepoAtlas.McpServer.Discovery;

/// <summary>
/// A path encountered during a scan that looked like a git repository but couldn't be read.
/// </summary>
/// <param name="Path">The path that was skipped.</param>
/// <param name="Reason">A human-readable explanation of why it was skipped.</param>
public sealed record SkippedPath(string Path, string Reason);
