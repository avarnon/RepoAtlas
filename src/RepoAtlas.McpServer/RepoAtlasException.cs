namespace RepoAtlas.McpServer;

/// <summary>
/// Base type for exceptions raised by RepoAtlas domain logic. Caught at the MCP tool/resource
/// boundary and translated into client-facing <see cref="ModelContextProtocol.McpException"/>s.
/// </summary>
public class RepoAtlasException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="RepoAtlasException"/>.
    /// </summary>
    /// <param name="message">A message describing the error.</param>
    public RepoAtlasException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="RepoAtlasException"/> wrapping an inner exception.
    /// </summary>
    /// <param name="message">A message describing the error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public RepoAtlasException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
