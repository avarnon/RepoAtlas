using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Models;

namespace RepoAtlas.McpServer;

/// <summary>
/// Full detail for a single cataloged repo: curated data merged with live git facts and README content.
/// </summary>
/// <param name="Id">The repo's id.</param>
/// <param name="Description">The curated description text.</param>
/// <param name="Tags">The curated tags.</param>
/// <param name="DependsOn">The repo's curated outgoing dependency links.</param>
/// <param name="Unreachable">Whether the repo's folder was not found on disk during the last rescan.</param>
/// <param name="Remotes">The repo's live git remotes. Empty if <paramref name="Unreachable"/> is <see langword="true"/>.</param>
/// <param name="CurrentBranch">The currently checked-out branch, or <see langword="null"/> if unreachable/unknown.</param>
/// <param name="HeadSha">The commit SHA of HEAD, or <see langword="null"/> if unreachable or the repo has no commits.</param>
/// <param name="IsFork">Whether the repo has a detected upstream remote distinct from "origin".</param>
/// <param name="UpstreamUrl">The URL of the detected upstream remote, or <see langword="null"/> if none.</param>
/// <param name="ReadmeContent">The repo's README.md content, or <see langword="null"/> if unreachable or it has none.</param>
public sealed record RepoDetail(
    string Id,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<DependencyLink> DependsOn,
    bool Unreachable,
    IReadOnlyList<GitRemote> Remotes,
    string? CurrentBranch,
    string? HeadSha,
    bool IsFork,
    string? UpstreamUrl,
    string? ReadmeContent);
