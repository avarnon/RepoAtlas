namespace RepoAtlas.McpServer.Discovery;

/// <summary>
/// Derives the stable "owner/repo" identifier used to key a repository in the catalog.
/// </summary>
public static class RepoIdDeriver
{
    /// <summary>
    /// Derives a repo id from its origin remote URL, falling back to the folder name
    /// when no origin remote is present or its URL can't be parsed into an owner/repo pair.
    /// </summary>
    /// <param name="originRemoteUrl">The URL of the repo's "origin" remote, or <see langword="null"/> if it has none.</param>
    /// <param name="folderName">The name of the folder the repo was found in, used as a fallback id.</param>
    /// <returns>The derived id, in "owner/repo" form when parseable, otherwise <paramref name="folderName"/>.</returns>
    public static string DeriveId(string? originRemoteUrl, string folderName)
    {
        if (originRemoteUrl is not null && TryParseOwnerRepo(originRemoteUrl) is string ownerRepo)
        {
            return ownerRepo;
        }

        return folderName;
    }

    /// <summary>
    /// Attempts to extract an "owner/repo" pair from a git remote URL, supporting HTTPS/HTTP/SSH
    /// URLs and SCP-style shorthand (e.g. <c>git@github.com:owner/repo.git</c>).
    /// </summary>
    /// <param name="remoteUrl">The remote URL to parse.</param>
    /// <returns>The "owner/repo" pair, or <see langword="null"/> if the URL doesn't match a known shape.</returns>
    public static string? TryParseOwnerRepo(string remoteUrl)
    {
        var trimmed = remoteUrl.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        string? path = null;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "https" or "http" or "ssh"))
        {
            path = uri.AbsolutePath.Trim('/');
        }
        else
        {
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex >= 0 && trimmed.Contains('@'))
            {
                path = trimmed[(colonIndex + 1)..].Trim('/');
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        return $"{segments[^2]}/{segments[^1]}";
    }
}
