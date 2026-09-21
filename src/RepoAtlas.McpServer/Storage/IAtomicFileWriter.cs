namespace RepoAtlas.McpServer.Storage;

/// <summary>
/// Writes text files atomically, so readers never observe a partially-written file.
/// </summary>
public interface IAtomicFileWriter
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> by writing to a temporary
    /// file first and then renaming it into place, creating any missing parent directories.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    /// <param name="content">The text content to write.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
}
