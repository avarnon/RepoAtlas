namespace RepoAtlas.McpServer;

/// <summary>
/// Thrown when a requested dependency link between two repos is invalid, e.g. a repo depending on itself.
/// </summary>
public sealed class InvalidDependencyException : RepoAtlasException
{
    /// <summary>
    /// Initializes a new instance of <see cref="InvalidDependencyException"/>.
    /// </summary>
    /// <param name="message">A message describing why the dependency is invalid.</param>
    public InvalidDependencyException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="InvalidDependencyException"/>, wrapping an inner exception.
    /// </summary>
    /// <param name="message">A message describing why the dependency is invalid.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InvalidDependencyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
