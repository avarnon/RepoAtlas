namespace RepoAtlas.McpServer;

/// <summary>
/// Thrown when an operation references a repo id that isn't in the catalog.
/// </summary>
public sealed class RepoNotFoundException : RepoAtlasException
{
    /// <summary>
    /// Initializes a new instance of <see cref="RepoNotFoundException"/> for the given repo id.
    /// </summary>
    /// <param name="id">The unknown repo id.</param>
    public RepoNotFoundException(string id) : base($"Unknown repo id: '{id}'")
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RepoNotFoundException"/> for the given repo id, wrapping an inner exception.
    /// </summary>
    /// <param name="id">The unknown repo id.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public RepoNotFoundException(string id, Exception innerException) : base($"Unknown repo id: '{id}'", innerException)
    {
    }
}
