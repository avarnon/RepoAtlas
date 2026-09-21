using System.Text;
using Microsoft.Extensions.Logging;

namespace RepoAtlas.McpServer.Storage;

/// <inheritdoc cref="IAtomicFileWriter" />
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    private readonly ILogger<AtomicFileWriter> _logger;
    private readonly Action<string, string> _move;

    /// <summary>
    /// Initializes a new instance of <see cref="AtomicFileWriter"/>.
    /// </summary>
    /// <param name="logger">Logger for write outcomes.</param>
    public AtomicFileWriter(ILogger<AtomicFileWriter> logger)
        : this(logger, static (source, destination) => File.Move(source, destination, overwrite: true))
    {
    }

    /// <summary>
    /// Test-only seam letting <see cref="MoveWithRetryAsync"/>'s retry behavior be exercised
    /// deterministically, independent of any platform's file-locking semantics.
    /// </summary>
    /// <param name="logger">Logger for write outcomes.</param>
    /// <param name="move">Replaces the <see cref="File.Move(string, string, bool)"/> call used to rename the temp file onto its destination.</param>
    internal AtomicFileWriter(ILogger<AtomicFileWriter> logger, Action<string, string> move)
    {
        _logger = logger;
        _move = move;
    }

    /// <inheritdoc />
    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("AtomicFileWriter.WriteAllTextAsync");
        activity?.SetTag("repoatlas.file.path", path);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // A unique-per-write suffix (rather than a fixed ".tmp") means two writers racing to write
        // the same destination path — whether two processes, or a retried call — never share a
        // temp file, so neither can observe or clobber the other's in-progress write.
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            await MoveWithRetryAsync(tempPath, path, cancellationToken);
            _logger.LogDebug("Wrote {ByteCount} bytes to {Path}.", Encoding.UTF8.GetByteCount(content), path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to atomically write to {Path}.", path);
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of the abandoned temp file; the original write failure above
                // is what the caller needs to see, so a failure to delete it here must not mask it.
                _logger.LogWarning(cleanupEx, "Failed to clean up abandoned temp file {TempPath}.", tempPath);
            }
            throw;
        }
    }

    /// <summary>
    /// Renames <paramref name="tempPath"/> onto <paramref name="path"/>, retrying briefly if the
    /// destination is transiently locked (e.g. by antivirus/indexing scanning the just-written temp
    /// file, or another writer's read racing this rename) before giving up and letting the final
    /// attempt's exception propagate. A sharing violation on Windows can surface as either
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> depending on how the
    /// destination is held open, so both are treated as transient here.
    /// </summary>
    private async Task MoveWithRetryAsync(string tempPath, string path, CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _move(tempPath, path);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(15 * attempt), cancellationToken);
            }
        }
    }
}
