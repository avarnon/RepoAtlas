namespace RepoAtlas.McpServer;

/// <summary>
/// Parses the <c>REPOATLAS_ALLOWED_ROOT_BASES</c> environment variable into a list of root bases.
/// </summary>
public static class AllowedRootBasesParser
{
    /// <summary>
    /// Splits <paramref name="raw"/> on <see cref="Path.PathSeparator"/> (<c>;</c> on Windows,
    /// <c>:</c> elsewhere), trimming each entry and discarding empty ones.
    /// </summary>
    /// <param name="raw">The raw environment variable value, or <see langword="null"/> if unset.</param>
    /// <returns>The parsed list of root bases, empty if <paramref name="raw"/> is null, empty, or whitespace-only.</returns>
    public static IReadOnlyList<string> Parse(string? raw) =>
        (raw ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
