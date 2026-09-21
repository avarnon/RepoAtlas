using System.Text;
using Microsoft.Extensions.Logging;

namespace RepoAtlas.McpServer;

/// <inheritdoc cref="IReadmeReader" />
public sealed class ReadmeReader : IReadmeReader
{
    /// <summary>
    /// Display cap for README content returned to an MCP client. Small enough that even a
    /// maxed-out README stays a modest slice of an LLM client's context budget (roughly 16k
    /// tokens at ~4 chars/token) rather than the ~250k tokens the previous 1 MiB cap allowed.
    /// </summary>
    private const long MaxReadmeBytes = 64 * 1024;

    private readonly ILogger<ReadmeReader> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ReadmeReader"/>.
    /// </summary>
    /// <param name="logger">Logger for oversized-README diagnostics.</param>
    public ReadmeReader(ILogger<ReadmeReader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string? Read(string repoPath)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("ReadmeReader.Read");
        activity?.SetTag("repoatlas.repo.path", repoPath);

        var readmePath = Directory.EnumerateFiles(repoPath)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), "README.md", StringComparison.OrdinalIgnoreCase));

        if (readmePath is null)
        {
            return null;
        }

        if (!IsConfinedToRepo(readmePath, repoPath))
        {
            _logger.LogWarning("README.md at {Path} is a symlink resolving outside the repo; refusing to read it.", readmePath);
            return null;
        }

        var info = new FileInfo(readmePath);
        if (info.Length > MaxReadmeBytes)
        {
            _logger.LogDebug("README.md at {Path} is {Bytes} bytes, exceeding the display limit; truncating.", readmePath, info.Length);
            var truncated = ReadUpToByteLimit(readmePath, (int)MaxReadmeBytes);
            return $"{truncated}\n\n[README.md truncated: showing the first {MaxReadmeBytes:N0} of {info.Length:N0} bytes.]";
        }

        return File.ReadAllText(readmePath);
    }

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> bytes of <paramref name="path"/> — never the whole
    /// file, which for an oversized README would defeat the point of a display cap — and decodes
    /// them as UTF-8, stopping short of a multi-byte sequence left incomplete by the cutoff rather
    /// than emitting a replacement character or a lone surrogate.
    /// </summary>
    private static string ReadUpToByteLimit(string path, int maxBytes)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[maxBytes];
        var totalRead = 0;
        int bytesRead;
        while (totalRead < maxBytes && (bytesRead = stream.Read(buffer, totalRead, maxBytes - totalRead)) > 0)
        {
            totalRead += bytesRead;
        }

        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[totalRead];
        decoder.Convert(buffer, 0, totalRead, chars, 0, chars.Length, flush: false, out _, out var charsUsed, out _);
        return new string(chars, 0, charsUsed);
    }

    /// <summary>
    /// Confirms that <paramref name="readmePath"/>, after resolving any symlink, still points
    /// somewhere inside <paramref name="repoPath"/> — so a README.md symlinked to a file elsewhere
    /// on disk (e.g. an SSH key or credentials file) can't be read out through the catalog.
    /// </summary>
    /// <remarks>
    /// A repo reached only via a symlinked directory (see <c>RepoDiscoverer</c>) is itself cataloged
    /// under that symlink's path, so <paramref name="repoPath"/> is resolved to its real location
    /// first. When README.md is a plain file (not itself a link), its target is then derived from
    /// that *resolved* repo path rather than <paramref name="readmePath"/> as given — comparing an
    /// unresolved readmePath (still expressed via the symlink) against a resolved repoPath would
    /// otherwise report a false escape for the common case. When README.md *is* a link, resolving
    /// it directly still correctly catches one escaping to outside the (resolved) repo.
    /// </remarks>
    private static bool IsConfinedToRepo(string readmePath, string repoPath)
    {
        var resolvedRepoPath = Directory.ResolveLinkTarget(repoPath, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(repoPath);
        var target = File.ResolveLinkTarget(readmePath, returnFinalTarget: true)?.FullName
            ?? Path.Combine(resolvedRepoPath, Path.GetFileName(readmePath));
        var repoFullPath = resolvedRepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return string.Equals(target, repoFullPath, comparison) ||
            target.StartsWith(repoFullPath + Path.DirectorySeparatorChar, comparison);
    }
}
