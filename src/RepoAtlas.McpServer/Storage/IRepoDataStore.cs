using RepoAtlas.McpServer.Models;

namespace RepoAtlas.McpServer.Storage;

/// <summary>
/// A concurrency-safe, durable store for the catalog's <see cref="DataStoreDocument"/>.
/// </summary>
public interface IRepoDataStore
{
    /// <summary>
    /// Runs <paramref name="read"/> against the current document under a read lock and returns its result.
    /// </summary>
    /// <typeparam name="T">The type of value projected from the document.</typeparam>
    /// <param name="read">A projection function applied to the current document.</param>
    /// <param name="cancellationToken">A token to cancel waiting for the lock.</param>
    /// <returns>The value returned by <paramref name="read"/>.</returns>
    Task<T> ReadAsync<T>(Func<DataStoreDocument, T> read, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="mutate"/> against the current document under an exclusive lock and
    /// persists the result. If <paramref name="mutate"/> throws, no changes are persisted.
    /// </summary>
    /// <param name="mutate">A function that mutates the document in place.</param>
    /// <param name="cancellationToken">A token to cancel waiting for the lock.</param>
    Task MutateAsync(Action<DataStoreDocument> mutate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the in-memory document and re-reads it from disk under an exclusive lock, so
    /// subsequent reads and mutations see edits made to the file by something other than this
    /// store instance (e.g. a hand edit, or another process).
    /// </summary>
    /// <param name="cancellationToken">A token to cancel waiting for the lock.</param>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
