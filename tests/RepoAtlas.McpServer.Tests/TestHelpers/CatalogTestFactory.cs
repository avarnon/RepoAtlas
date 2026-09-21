using Microsoft.Extensions.Logging.Abstractions;
using RepoAtlas.McpServer.Discovery;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Storage;

namespace RepoAtlas.McpServer.Tests.TestHelpers;

public sealed class FakeGitFactsReader(Func<string, GitFacts> read) : IGitFactsReader
{
    public IReadOnlyList<GitRemote> GetRemotes(string repoPath) => throw new NotSupportedException("not used");

    public GitFacts Read(string repoPath) => read(repoPath);
}

public sealed class FakeReadmeReader(Func<string, string?> read) : IReadmeReader
{
    public string? Read(string repoPath) => read(repoPath);
}

public sealed class FakeRepoDiscoverer(Func<IEnumerable<string>, ScanResult> scan) : IRepoDiscoverer
{
    public ScanResult Scan(IEnumerable<string> roots) => scan(roots);
}

public static class CatalogTestFactory
{
    private static Func<string, T> NotUsed<T>() => _ => throw new InvalidOperationException("not used");

    public static YamlDataStore NewStore(string path) =>
        new(path, new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance), NullLogger<YamlDataStore>.Instance);

    public static RepoCatalog NewCatalog(
        IRepoDataStore store,
        Func<IEnumerable<string>, ScanResult>? scan = null,
        Func<string, GitFacts>? readGitFacts = null,
        Func<string, string?>? readReadme = null,
        IReadOnlyList<string>? allowedRootBases = null) =>
        new(
            store,
            new FakeRepoDiscoverer(scan ?? (_ => throw new InvalidOperationException("not used"))),
            new FakeGitFactsReader(readGitFacts ?? NotUsed<GitFacts>()),
            new FakeReadmeReader(readReadme ?? NotUsed<string?>()),
            NullLogger<RepoCatalog>.Instance,
            allowedRootBases);
}
