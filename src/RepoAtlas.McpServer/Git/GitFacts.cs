namespace RepoAtlas.McpServer.Git;

/// <summary>
/// Live facts read directly from a repository's on-disk git state.
/// </summary>
/// <param name="Remotes">The repo's configured remotes.</param>
/// <param name="CurrentBranch">The friendly name of the currently checked-out branch, or <see langword="null"/> if detached/unknown.</param>
/// <param name="HeadSha">The commit SHA of HEAD, or <see langword="null"/> if the repo has no commits.</param>
/// <param name="IsFork">Whether an upstream remote (distinct from "origin") was found, indicating the repo is a fork.</param>
/// <param name="UpstreamUrl">The URL of the detected upstream remote, or <see langword="null"/> if none was found.</param>
public sealed record GitFacts(
    IReadOnlyList<GitRemote> Remotes,
    string? CurrentBranch,
    string? HeadSha,
    bool IsFork,
    string? UpstreamUrl);
