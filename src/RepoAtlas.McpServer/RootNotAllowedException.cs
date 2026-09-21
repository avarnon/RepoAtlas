namespace RepoAtlas.McpServer;

/// <summary>
/// Thrown when a root directory passed to <see cref="IRepoCatalog.AddRootAsync"/> is refused:
/// either it is a filesystem root (scanning it would walk an entire drive/volume), or it falls
/// outside an operator-configured allowlist of root bases.
/// </summary>
public sealed class RootNotAllowedException : RepoAtlasException
{
    /// <summary>
    /// Initializes a new instance of <see cref="RootNotAllowedException"/>.
    /// </summary>
    /// <param name="root">The rejected, normalized root path.</param>
    /// <param name="reason">Why the root was rejected.</param>
    public RootNotAllowedException(string root, string reason) : base($"Root '{root}' was not added: {reason}")
    {
    }
}
