namespace RepoAtlas.McpServer;

/// <summary>
/// The catalog of known git repositories: curated metadata (description, tags, dependency
/// links) merged with live git facts, backed by durable storage and kept in sync with disk via
/// <see cref="RescanAsync"/>.
/// </summary>
public interface IRepoCatalog
{
    /// <summary>
    /// Lists all known repos, sorted by id.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>Summaries of every cataloged repo.</returns>
    Task<IReadOnlyList<RepoSummary>> ListReposAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets full detail for one repo, merging curated data with live git facts and README content.
    /// </summary>
    /// <param name="id">The repo's id.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The repo's full detail.</returns>
    /// <exception cref="RepoNotFoundException"><paramref name="id"/> is not in the catalog.</exception>
    Task<RepoDetail> GetRepoDetailAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a repo's curated description.
    /// </summary>
    /// <param name="id">The repo's id.</param>
    /// <param name="description">The new description text.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <exception cref="RepoNotFoundException"><paramref name="id"/> is not in the catalog.</exception>
    Task UpdateDescriptionAsync(string id, string description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a tag to a repo, if it isn't already present.
    /// </summary>
    /// <param name="id">The repo's id.</param>
    /// <param name="tag">The tag to add.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <exception cref="RepoNotFoundException"><paramref name="id"/> is not in the catalog.</exception>
    Task AddTagAsync(string id, string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a tag from a repo, if present. A no-op if the tag isn't present.
    /// </summary>
    /// <param name="id">The repo's id.</param>
    /// <param name="tag">The tag to remove.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <exception cref="RepoNotFoundException"><paramref name="id"/> is not in the catalog.</exception>
    Task RemoveTagAsync(string id, string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds (or updates the note on) a directed dependency link: <paramref name="fromId"/>'s code depends on <paramref name="toId"/>.
    /// </summary>
    /// <param name="fromId">The dependent repo's id.</param>
    /// <param name="toId">The depended-upon repo's id.</param>
    /// <param name="note">An optional note about the dependency.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <exception cref="InvalidDependencyException"><paramref name="fromId"/> equals <paramref name="toId"/>.</exception>
    /// <exception cref="RepoNotFoundException">Either <paramref name="fromId"/> or <paramref name="toId"/> is not in the catalog.</exception>
    Task AddDependencyAsync(string fromId, string toId, string? note, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a dependency link between two repos. A no-op if the link doesn't exist.
    /// </summary>
    /// <param name="fromId">The dependent repo's id.</param>
    /// <param name="toId">The depended-upon repo's id.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <exception cref="RepoNotFoundException"><paramref name="fromId"/> is not in the catalog.</exception>
    Task RemoveDependencyAsync(string fromId, string toId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-scans the configured root directories for git repositories, adding newly found repos
    /// and flagging repos as reachable/unreachable based on whether their folder was found.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the rescan.</param>
    /// <returns>A summary of what changed.</returns>
    Task<RescanSummary> RescanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a root directory to scan for git repositories, if it isn't already configured.
    /// The path is resolved to its full form before being compared and stored.
    /// </summary>
    /// <param name="root">The root directory to add.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <exception cref="RootNotAllowedException">
    /// <paramref name="root"/> is a filesystem root, or falls outside the allowlist configured via
    /// the <c>REPOATLAS_ALLOWED_ROOT_BASES</c> environment variable, if set.
    /// </exception>
    Task AddRootAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a configured root directory, if present. A no-op if the root isn't configured.
    /// </summary>
    /// <param name="root">The root directory to remove.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    Task RemoveRootAsync(string root, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the configured root directories, sorted.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    Task<IReadOnlyList<string>> ListRootsAsync(CancellationToken cancellationToken = default);
}
