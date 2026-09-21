using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using RepoAtlas.McpServer.Git;

namespace RepoAtlas.McpServer.Discovery;

/// <inheritdoc cref="IRepoDiscoverer" />
public sealed class RepoDiscoverer : IRepoDiscoverer
{
    /// <summary>
    /// Default cap on recursion depth below any configured root, as a backstop against runaway
    /// trees (e.g. a directory cycle formed by a symlink that somehow evades <see cref="IsReparsePoint"/>).
    /// </summary>
    private const int DefaultMaxDepth = 64;

    /// <summary>
    /// Directory names never worth descending into: dependency/package caches and build output
    /// that are typically enormous, never contain a top-level repo worth cataloging on their own,
    /// and would otherwise dominate every scan's cost. A root configured with one of these names
    /// directly (depth 0) is still scanned — this only applies to directories found while recursing.
    /// </summary>
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", ".venv", "venv",
    };

    private readonly IGitFactsReader _gitFactsReader;
    private readonly ILogger<RepoDiscoverer> _logger;
    private readonly int _maxDepth;

    /// <summary>
    /// Initializes a new instance of <see cref="RepoDiscoverer"/>.
    /// </summary>
    /// <param name="gitFactsReader">Used to read remotes for candidate repo directories while scanning.</param>
    /// <param name="logger">Logger for scan progress and skipped paths.</param>
    /// <param name="maxDepth">Caps recursion depth below any configured root; defaults to <see cref="DefaultMaxDepth"/>.</param>
    public RepoDiscoverer(IGitFactsReader gitFactsReader, ILogger<RepoDiscoverer> logger, int maxDepth = DefaultMaxDepth)
    {
        _gitFactsReader = gitFactsReader;
        _logger = logger;
        _maxDepth = maxDepth;
    }

    /// <inheritdoc />
    public ScanResult Scan(IEnumerable<string> roots)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("RepoDiscoverer.Scan");

        var found = new List<DiscoveredRepo>();
        var missing = new List<string>();
        var skipped = new List<SkippedPath>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                _logger.LogWarning("Configured root {Root} does not exist; skipping.", root);
                missing.Add(root);
                continue;
            }

            ScanDirectory(root, found, skipped);
        }

        _logger.LogInformation("Scan found {RepoCount} repo(s), {SkippedCount} skipped, {MissingCount} missing root(s).",
            found.Count, skipped.Count, missing.Count);

        return new ScanResult(found, missing, skipped);
    }

    /// <summary>
    /// Recursively walks <paramref name="directory"/>, recording it as a found repo (without
    /// descending further) if <see cref="IsGitRepository"/> recognizes it as one — a normal repo,
    /// a linked worktree/submodule, or a bare repo — or recursing into its subdirectories otherwise.
    /// </summary>
    /// <param name="directory">The directory to inspect.</param>
    /// <param name="found">Accumulates repos found so far.</param>
    /// <param name="skipped">Accumulates paths that looked like repos, hit an access/IO error, or were not followed.</param>
    /// <param name="depth">Recursion depth below the configured root; 0 for the root itself.</param>
    private void ScanDirectory(string directory, List<DiscoveredRepo> found, List<SkippedPath> skipped, int depth = 0)
    {
        if (depth > _maxDepth)
        {
            _logger.LogWarning("Directory {Directory} exceeds the max scan depth of {MaxDepth}; skipping.", directory, _maxDepth);
            skipped.Add(new SkippedPath(directory, $"exceeds max scan depth of {_maxDepth}"));
            return;
        }

        if (IsGitRepository(directory))
        {
            string? originUrl;
            try
            {
                originUrl = _gitFactsReader.GetRemotes(directory)
                    .FirstOrDefault(r => r.Name == "origin")?.Url;
            }
            catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Skipping {Directory}: {Reason}.", directory, ex.Message);
                skipped.Add(new SkippedPath(directory, ex.Message));
                return;
            }

            var id = RepoIdDeriver.DeriveId(originUrl, new DirectoryInfo(directory).Name);
            found.Add(new DiscoveredRepo(id, directory));
            return;
        }

        // A repo symlinked directly into a scan root is still worth cataloging (it was already
        // checked for .git above), so this only refuses to descend *through* a link once we
        // know it isn't a repo itself — that's the only case that risks an infinite recursion
        // cycle (e.g. a symlink pointing back at an ancestor directory). depth 0 is exempt:
        // a configured root being a symlink carries no cycle risk on its own.
        if (depth > 0 && IsReparsePoint(directory, out var reparseError))
        {
            var reason = reparseError is null ? "symlink or junction, not followed" : reparseError.Message;
            _logger.LogInformation(reparseError, "Skipping {Directory}: {Reason}.", directory, reason);
            skipped.Add(new SkippedPath(directory, reason));
            return;
        }

        if (depth > 0 && ExcludedDirectoryNames.Contains(Path.GetFileName(directory)))
        {
            _logger.LogInformation("Skipping {Directory}: excluded directory name.", directory);
            skipped.Add(new SkippedPath(directory, "excluded directory name"));
            return;
        }

        IEnumerable<string> subdirectories;
        try
        {
            subdirectories = Directory.EnumerateDirectories(directory).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "Skipping {Directory}: {Reason}.", directory, ex.Message);
            skipped.Add(new SkippedPath(directory, ex.Message));
            return;
        }

        foreach (var subdirectory in subdirectories)
        {
            ScanDirectory(subdirectory, found, skipped, depth + 1);
        }
    }

    /// <summary>
    /// Determines whether <paramref name="directory"/> is itself a git repository: a normal
    /// repo (a <c>.git</c> subdirectory), a linked worktree or submodule (a <c>.git</c> *file*
    /// pointing at the real git dir elsewhere), or a bare repo (no <c>.git</c> entry at all —
    /// its <c>HEAD</c>/<c>objects</c>/<c>refs</c> sit directly in the directory).
    /// </summary>
    private static bool IsGitRepository(string directory)
    {
        var gitPath = Path.Combine(directory, ".git");
        if (Directory.Exists(gitPath) || File.Exists(gitPath))
        {
            return true;
        }

        return File.Exists(Path.Combine(directory, "HEAD"))
            && Directory.Exists(Path.Combine(directory, "objects"))
            && Directory.Exists(Path.Combine(directory, "refs"));
    }

    /// <summary>
    /// Determines whether <paramref name="path"/> is a symlink or junction, so callers can
    /// avoid following it into a potential directory cycle.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <param name="error">
    /// The exception that made this fail closed, or <see langword="null"/> if <paramref name="path"/>'s
    /// attributes were read successfully (whether or not it turned out to be a reparse point).
    /// </param>
    /// <remarks>
    /// Fails closed: if <paramref name="path"/>'s attributes can't be read (e.g. it was deleted
    /// between being enumerated and being checked here), this reports it as a reparse point rather
    /// than as a plain directory, so an unreadable path is skipped rather than risk being descended
    /// into as if it were known-safe — while still surfacing the real error via <paramref name="error"/>
    /// so a caller's skip reason doesn't misreport an IO/access failure as "symlink, not followed".
    /// </remarks>
    internal static bool IsReparsePoint(string path, out Exception? error)
    {
        try
        {
            error = null;
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            error = ex;
            return true;
        }
    }
}
