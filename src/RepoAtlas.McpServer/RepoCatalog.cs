using System.Diagnostics;
using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using RepoAtlas.McpServer.Discovery;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Models;
using RepoAtlas.McpServer.Storage;

namespace RepoAtlas.McpServer;

/// <inheritdoc cref="IRepoCatalog" />
public sealed class RepoCatalog : IRepoCatalog
{
    /// <summary>
    /// Compares root paths for equality/dedup: case-insensitive on Windows (whose filesystems
    /// are case-insensitive by default), case-sensitive elsewhere.
    /// </summary>
    private static readonly StringComparer RootPathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// <see cref="StringComparison"/> counterpart to <see cref="RootPathComparer"/>, for APIs
    /// (like <see cref="string.StartsWith(string, StringComparison)"/>) that don't accept a <see cref="StringComparer"/>.
    /// </summary>
    private static readonly StringComparison RootPathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly IRepoDataStore _store;
    private readonly IRepoDiscoverer _discoverer;
    private readonly IGitFactsReader _gitFactsReader;
    private readonly IReadmeReader _readmeReader;
    private readonly ILogger<RepoCatalog> _logger;
    private readonly IReadOnlyList<string> _allowedRootBases;

    /// <summary>
    /// Initializes a new instance of <see cref="RepoCatalog"/>.
    /// </summary>
    /// <param name="store">The durable store backing curated repo metadata.</param>
    /// <param name="discoverer">Used to scan configured roots for repos during <see cref="RescanAsync"/>.</param>
    /// <param name="gitFactsReader">Used to read live git facts for a repo's detail view.</param>
    /// <param name="readmeReader">Used to read a repo's README content for its detail view.</param>
    /// <param name="logger">Logger for rescan outcomes.</param>
    /// <param name="allowedRootBases">
    /// An optional, operator-configured allowlist of root bases. When non-empty, <see cref="AddRootAsync"/>
    /// refuses any root that doesn't fall under one of these bases — a defense against an MCP
    /// client being steered (e.g. by injected content read from a cataloged repo) into scanning
    /// an operator-unintended part of the filesystem. Empty (the default) means unrestricted.
    /// </param>
    public RepoCatalog(
        IRepoDataStore store,
        IRepoDiscoverer discoverer,
        IGitFactsReader gitFactsReader,
        IReadmeReader readmeReader,
        ILogger<RepoCatalog> logger,
        IReadOnlyList<string>? allowedRootBases = null)
    {
        _store = store;
        _discoverer = discoverer;
        _gitFactsReader = gitFactsReader;
        _readmeReader = readmeReader;
        _logger = logger;
        _allowedRootBases = (allowedRootBases ?? Array.Empty<string>()).Select(Path.GetFullPath).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepoSummary>> ListReposAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("RepoCatalog.ListRepos");

        return await _store.ReadAsync(doc => (IReadOnlyList<RepoSummary>)doc.Repos
            .Select(kvp => new RepoSummary(kvp.Key, kvp.Value.Description, kvp.Value.Tags.ToList(), kvp.Value.Unreachable))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RepoDetail> GetRepoDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("RepoCatalog.GetRepoDetail");
        activity?.SetTag("repoatlas.repo.id", id);

        var snapshot = await _store.ReadAsync(doc =>
        {
            if (!doc.Repos.TryGetValue(id, out var e))
            {
                return null;
            }

            return new RepoEntry
            {
                Path = e.Path,
                Description = e.Description,
                Tags = e.Tags.ToList(),
                DependsOn = e.DependsOn.Select(d => new DependencyLink { Id = d.Id, Note = d.Note }).ToList(),
                Unreachable = e.Unreachable,
            };
        }, cancellationToken);

        if (snapshot is null)
        {
            throw new RepoNotFoundException(id);
        }

        if (snapshot.Unreachable)
        {
            return new RepoDetail(id, snapshot.Description, snapshot.Tags, snapshot.DependsOn, true,
                Array.Empty<GitRemote>(), null, null, false, null, null);
        }

        GitFacts facts;
        string? readme;
        try
        {
            facts = _gitFactsReader.Read(snapshot.Path);
            readme = _readmeReader.Read(snapshot.Path);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            // The catalog's Unreachable flag is only refreshed by Rescan, so a repo whose
            // folder vanished since the last rescan reaches here with Unreachable still false.
            // Report it the same way an already-flagged-unreachable repo reads, instead of
            // letting a raw LibGit2Sharp/IO exception escape to the MCP client.
            _logger.LogWarning(ex, "Repo {Id} at {Path} could not be read; reporting as unreachable.", id, snapshot.Path);
            return new RepoDetail(id, snapshot.Description, snapshot.Tags, snapshot.DependsOn, true,
                Array.Empty<GitRemote>(), null, null, false, null, null);
        }

        return new RepoDetail(id, snapshot.Description, snapshot.Tags, snapshot.DependsOn, false,
            facts.Remotes, facts.CurrentBranch, facts.HeadSha, facts.IsFork, facts.UpstreamUrl, readme);
    }

    /// <inheritdoc />
    public async Task UpdateDescriptionAsync(string id, string description, CancellationToken cancellationToken = default)
    {
        await MutateExistingAsync(id, entry => entry.Description = description, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddTagAsync(string id, string tag, CancellationToken cancellationToken = default)
    {
        await MutateExistingAsync(id, entry =>
        {
            if (!entry.Tags.Contains(tag, StringComparer.Ordinal))
            {
                entry.Tags.Add(tag);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveTagAsync(string id, string tag, CancellationToken cancellationToken = default)
    {
        await MutateExistingAsync(id, entry => entry.Tags.RemoveAll(t => StringComparer.Ordinal.Equals(t, tag)), cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddDependencyAsync(string fromId, string toId, string? note, CancellationToken cancellationToken = default)
    {
        if (fromId == toId)
        {
            throw new InvalidDependencyException("A repo cannot depend on itself.");
        }

        await _store.MutateAsync(doc =>
        {
            if (!doc.Repos.TryGetValue(fromId, out var fromEntry))
            {
                throw new RepoNotFoundException(fromId);
            }

            if (!doc.Repos.ContainsKey(toId))
            {
                throw new RepoNotFoundException(toId);
            }

            var existing = fromEntry.DependsOn.FirstOrDefault(d => d.Id == toId);
            if (existing is not null)
            {
                existing.Note = note;
            }
            else
            {
                fromEntry.DependsOn.Add(new DependencyLink { Id = toId, Note = note });
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RemoveDependencyAsync(string fromId, string toId, CancellationToken cancellationToken = default)
    {
        await MutateExistingAsync(fromId, entry => entry.DependsOn.RemoveAll(d => d.Id == toId), cancellationToken);
    }

    /// <summary>
    /// Applies <paramref name="mutate"/> to an existing repo entry, throwing if <paramref name="id"/> isn't in the catalog.
    /// </summary>
    private async Task MutateExistingAsync(string id, Action<RepoEntry> mutate, CancellationToken cancellationToken)
    {
        await _store.MutateAsync(doc =>
        {
            if (!doc.Repos.TryGetValue(id, out var entry))
            {
                throw new RepoNotFoundException(id);
            }

            mutate(entry);
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RescanSummary> RescanAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("RepoCatalog.Rescan");
        var stopwatch = Stopwatch.StartNew();

        // Roots are commonly hand-edited directly in the YAML file (or curated via AddRoot/
        // RemoveRoot from a prior process); reload from disk so this rescan sees them instead
        // of clobbering them with a stale in-memory document on the next mutate.
        await _store.ReloadAsync(cancellationToken);
        var roots = await _store.ReadAsync(doc => doc.Roots.ToList(), cancellationToken);
        var scanResult = _discoverer.Scan(roots);

        var foundById = new Dictionary<string, string>();
        var skipped = scanResult.Skipped.Select(s => $"{s.Path} ({s.Reason})").ToList();

        foreach (var group in scanResult.Repos.GroupBy(r => r.Id))
        {
            var ordered = group.OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
            foundById[group.Key] = ordered[0].Path;

            skipped.AddRange(ordered.Skip(1)
                .Select(dropped => $"{dropped.Path} (duplicate id '{dropped.Id}', kept '{ordered[0].Path}')"));
        }

        var added = new List<string>();
        var newlyUnreachable = new List<string>();
        var newlyReachable = new List<string>();

        await _store.MutateAsync(doc =>
        {
            foreach (var (id, path) in foundById)
            {
                if (!doc.Repos.TryGetValue(id, out var entry))
                {
                    doc.Repos[id] = new RepoEntry { Path = path, Unreachable = false };
                    added.Add(id);
                }
                else
                {
                    entry.Path = path;
                    if (entry.Unreachable)
                    {
                        entry.Unreachable = false;
                        newlyReachable.Add(id);
                    }
                }
            }

            foreach (var (id, entry) in doc.Repos)
            {
                if (!foundById.ContainsKey(id) && !entry.Unreachable)
                {
                    entry.Unreachable = true;
                    newlyUnreachable.Add(id);
                }
            }
        }, cancellationToken);

        stopwatch.Stop();
        RepoAtlasTelemetry.RescanDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);
        _logger.LogInformation(
            "Rescan complete in {ElapsedMs}ms: {AddedCount} added, {NewlyUnreachableCount} newly unreachable, {NewlyReachableCount} newly reachable, {SkippedCount} skipped.",
            stopwatch.Elapsed.TotalMilliseconds, added.Count, newlyUnreachable.Count, newlyReachable.Count, skipped.Count);

        return new RescanSummary(added, newlyUnreachable, newlyReachable, scanResult.MissingRoots, skipped);
    }

    /// <inheritdoc />
    public async Task AddRootAsync(string root, CancellationToken cancellationToken = default)
    {
        var normalized = Path.GetFullPath(root);

        if (IsFileSystemRoot(normalized))
        {
            throw new RootNotAllowedException(normalized, "it is a filesystem root; scanning it would walk the entire drive/volume.");
        }

        if (_allowedRootBases.Count > 0 && !_allowedRootBases.Any(b => IsUnderBase(normalized, b)))
        {
            throw new RootNotAllowedException(normalized, "it is outside the operator-configured allowed root bases.");
        }

        await _store.MutateAsync(doc =>
        {
            if (!doc.Roots.Contains(normalized, RootPathComparer))
            {
                doc.Roots.Add(normalized);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Determines whether <paramref name="normalized"/> is a drive/volume root (e.g. <c>C:\</c> or <c>/</c>),
    /// which <see cref="AddRootAsync"/> refuses since scanning it would walk the entire filesystem.
    /// </summary>
    private static bool IsFileSystemRoot(string normalized) =>
        RootPathComparer.Equals(Path.GetPathRoot(normalized), normalized);

    /// <summary>
    /// Determines whether <paramref name="normalized"/> is <paramref name="basePath"/> or nested under it.
    /// </summary>
    private static bool IsUnderBase(string normalized, string basePath)
    {
        var normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return RootPathComparer.Equals(normalized, normalizedBase) ||
            normalized.StartsWith(normalizedBase + Path.DirectorySeparatorChar, RootPathComparison);
    }

    /// <inheritdoc />
    public async Task RemoveRootAsync(string root, CancellationToken cancellationToken = default)
    {
        var normalized = Path.GetFullPath(root);

        await _store.MutateAsync(doc => doc.Roots.RemoveAll(r => RootPathComparer.Equals(r, normalized)), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListRootsAsync(CancellationToken cancellationToken = default)
    {
        return await _store.ReadAsync(doc => (IReadOnlyList<string>)doc.Roots
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList(), cancellationToken);
    }
}
