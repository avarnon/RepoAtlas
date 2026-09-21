using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using RepoAtlas.McpServer.Discovery;

namespace RepoAtlas.McpServer.Git;

/// <inheritdoc cref="IGitFactsReader" />
public sealed class GitFactsReader : IGitFactsReader
{
    private readonly ILogger<GitFactsReader> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GitFactsReader"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostic detail about facts read from each repo.</param>
    public GitFactsReader(ILogger<GitFactsReader> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<GitRemote> GetRemotes(string repoPath)
    {
        using var repo = new Repository(repoPath);
        return MapRemotes(repo.Network.Remotes);
    }

    /// <inheritdoc />
    public GitFacts Read(string repoPath)
    {
        using var activity = RepoAtlasTelemetry.ActivitySource.StartActivity("GitFactsReader.Read");
        activity?.SetTag("repoatlas.repo.path", repoPath);

        using var repo = new Repository(repoPath);

        var remotes = MapRemotes(repo.Network.Remotes);

        var origin = remotes.FirstOrDefault(r => r.Name == "origin");
        var originOwner = origin is null ? null : RepoIdDeriver.TryParseOwnerRepo(origin.Url);

        string? upstreamUrl = remotes.FirstOrDefault(r => r.Name == "upstream")?.Url;

        if (upstreamUrl is null && originOwner is not null)
        {
            upstreamUrl = remotes.FirstOrDefault(r =>
                    r.Name != "origin" &&
                    RepoIdDeriver.TryParseOwnerRepo(r.Url) is string otherOwner &&
                    otherOwner != originOwner)
                ?.Url;
        }

        _logger.LogDebug("Read git facts for {Path}: branch={Branch}, isFork={IsFork}.", repoPath, repo.Head?.FriendlyName, upstreamUrl is not null);

        return new GitFacts(
            remotes,
            repo.Head?.FriendlyName,
            repo.Head?.Tip?.Sha,
            upstreamUrl is not null,
            upstreamUrl);
    }

    /// <summary>
    /// Projects LibGit2Sharp remotes into <see cref="GitRemote"/> records, stripping any embedded credentials from their URLs.
    /// </summary>
    private static IReadOnlyList<GitRemote> MapRemotes(IEnumerable<Remote> remotes) =>
        remotes.Select(r => new GitRemote(r.Name, StripUserInfo(r.Url))).ToList();

    /// <summary>
    /// Removes embedded username/password credentials from an HTTP(S)/SSH URL, if present.
    /// SCP-style URLs (e.g. <c>git@github.com:owner/repo.git</c>) are left unchanged since they
    /// carry no separable credential component.
    /// </summary>
    private static string StripUserInfo(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme is "https" or "http" or "ssh" &&
            !string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
            return builder.Uri.ToString();
        }

        return url;
    }
}
