using Microsoft.Extensions.Logging.Abstractions;
using RepoAtlas.McpServer.Discovery;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Models;
using RepoAtlas.McpServer.Storage;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests;

public class RepoCatalogRescanTests
{
    private static RepoCatalog NewCatalog(YamlDataStore store, Func<IEnumerable<string>, ScanResult> scanRoots) =>
        CatalogTestFactory.NewCatalog(store, scan: scanRoots);

    [Fact]
    public async Task RescanAsync_AddsNewlyFoundRepos()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var scanResult = new ScanResult(
                [new DiscoveredRepo("owner/repo", @"C:\owner\repo")],
                Array.Empty<string>(),
                Array.Empty<SkippedPath>());
            var catalog = NewCatalog(store, _ => scanResult);

            var summary = await catalog.RescanAsync();

            Assert.Equal(["owner/repo"], summary.Added);
            var path = await store.ReadAsync(doc => doc.Repos["owner/repo"].Path);
            Assert.Equal(@"C:\owner\repo", path);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_CalledTwiceWithSameResult_IsIdempotent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var scanResult = new ScanResult(
                [new DiscoveredRepo("owner/repo", @"C:\owner\repo")],
                Array.Empty<string>(),
                Array.Empty<SkippedPath>());
            var catalog = NewCatalog(store, _ => scanResult);

            await catalog.RescanAsync();
            var second = await catalog.RescanAsync();

            Assert.Empty(second.Added);
            Assert.Empty(second.NewlyUnreachable);
            Assert.Empty(second.NewlyReachable);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_RepoNoLongerFound_IsFlaggedUnreachableOnceAndPreservesCuratedData()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            await store.MutateAsync(doc =>
            {
                doc.Repos["owner/repo"] = new RepoEntry
                {
                    Path = @"C:\owner\repo",
                    Description = "curated description",
                    Tags = { "keep-me" },
                };
            });
            var emptyResult = new ScanResult(Array.Empty<DiscoveredRepo>(), Array.Empty<string>(), Array.Empty<SkippedPath>());
            var catalog = NewCatalog(store, _ => emptyResult);

            var first = await catalog.RescanAsync();
            var second = await catalog.RescanAsync();

            Assert.Equal(["owner/repo"], first.NewlyUnreachable);
            Assert.Empty(second.NewlyUnreachable);

            var entry = await store.ReadAsync(doc => doc.Repos["owner/repo"]);
            Assert.True(entry.Unreachable);
            Assert.Equal("curated description", entry.Description);
            Assert.Contains("keep-me", entry.Tags);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_RepoReappearsAfterBeingUnreachable_IsFlaggedReachableAgain()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            await store.MutateAsync(doc =>
            {
                doc.Repos["owner/repo"] = new RepoEntry { Path = @"C:\owner\repo", Unreachable = true };
            });
            var scanResult = new ScanResult(
                [new DiscoveredRepo("owner/repo", @"C:\owner\repo")],
                Array.Empty<string>(),
                Array.Empty<SkippedPath>());
            var catalog = NewCatalog(store, _ => scanResult);

            var summary = await catalog.RescanAsync();

            Assert.Equal(new[] { "owner/repo" }, summary.NewlyReachable);
            var entry = await store.ReadAsync(doc => doc.Repos["owner/repo"]);
            Assert.False(entry.Unreachable);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_PassesThroughMissingRoots()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var scanResult = new ScanResult(Array.Empty<DiscoveredRepo>(), [@"C:\gone"], Array.Empty<SkippedPath>());
            var catalog = NewCatalog(store, _ => scanResult);

            var summary = await catalog.RescanAsync();

            Assert.Equal([@"C:\gone"], summary.MissingRoots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_PassesThroughDiscovererReportedSkips()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var scanResult = new ScanResult(
                Array.Empty<DiscoveredRepo>(),
                Array.Empty<string>(),
                [new SkippedPath(@"C:\too\long\path", "path too long")]);
            var catalog = NewCatalog(store, _ => scanResult);

            var summary = await catalog.RescanAsync();

            Assert.Contains(summary.Skipped, s => s.Contains(@"C:\too\long\path") && s.Contains("path too long"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_PicksUpRootAddedThroughASeparateStoreInstanceSinceConstruction()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var dataPath = Path.Combine(tempDir.FullName, "data.yaml");
            var store = CatalogTestFactory.NewStore(dataPath);
            var newRoot = Path.Combine(tempDir.FullName, "code");
            var scanResult = new ScanResult(
                [new DiscoveredRepo("owner/repo", Path.Combine(newRoot, "repo"))],
                Array.Empty<string>(),
                Array.Empty<SkippedPath>());
            var seenRoots = new List<string>();
            var catalog = NewCatalog(store, roots =>
            {
                seenRoots.AddRange(roots);
                return scanResult;
            });

            // Simulate a root added by hand-editing the YAML file (or by a separate process)
            // after `store`'s in-memory document was already loaded.
            var externalWriter = CatalogTestFactory.NewStore(dataPath);
            await externalWriter.MutateAsync(doc => doc.Roots.Add(newRoot));

            var summary = await catalog.RescanAsync();

            Assert.Contains(newRoot, seenRoots);
            Assert.Equal(["owner/repo"], summary.Added);
            var persistedRoots = await store.ReadAsync(doc => doc.Roots);
            Assert.Contains(newRoot, persistedRoots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_TwoDiscoveredReposShareTheSameId_KeepsOneAndReportsTheCollision()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var scanResult = new ScanResult(
                [
                    new DiscoveredRepo("myrepo", @"C:\rootA\myrepo"),
                    new DiscoveredRepo("myrepo", @"C:\rootB\myrepo"),
                ],
                Array.Empty<string>(),
                Array.Empty<SkippedPath>());
            var catalog = NewCatalog(store, _ => scanResult);

            var summary = await catalog.RescanAsync();

            var storedIds = await store.ReadAsync(doc => doc.Repos.Keys.ToList());
            Assert.Equal(new[] { "myrepo" }, storedIds);
            var storedPath = await store.ReadAsync(doc => doc.Repos["myrepo"].Path);
            Assert.Equal(@"C:\rootA\myrepo", storedPath);
            Assert.Contains(summary.Skipped, s => s.Contains(@"C:\rootB\myrepo") && s.Contains("myrepo"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RescanAsync_ThroughARealDiscoverer_ALinkedWorktreeSharesItsMainRepoIdAndFallsIntoTheExistingDuplicateHandling()
    {
        // A linked worktree shares its main repo's remotes (they're the same underlying
        // repository, just checked out twice), so RepoDiscoverer.IsGitRepository correctly
        // finds both, but RepoIdDeriver then derives the *same* id for both — same as two
        // unrelated directories that happen to collide (RescanAsync_TwoDiscoveredReposShareTheSameId
        // above). This end-to-end test, using the real discoverer and git reader rather than fakes,
        // confirms that pairing lands in that existing, already-tested duplicate-id handling
        // instead of the worktree being lost or the scan recursing into its internals — the two
        // failure modes the discoverer-level fix actually targets.
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var mainPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "main-repo",
                remotes: [("origin", "https://github.com/owner/main-repo.git")], withCommit: true);
            var worktreePath = Path.Combine(tempDir.FullName, "main-repo-worktree");
            using (var mainRepo = new LibGit2Sharp.Repository(mainPath))
            {
                mainRepo.Worktrees.Add("main-repo-worktree", worktreePath, isLocked: false);
            }

            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            await store.MutateAsync(doc => doc.Roots.Add(tempDir.FullName));
            var discoverer = new RepoDiscoverer(new GitFactsReader(NullLogger<GitFactsReader>.Instance), NullLogger<RepoDiscoverer>.Instance);
            var catalog = CatalogTestFactory.NewCatalog(store, scan: discoverer.Scan);

            var summary = await catalog.RescanAsync();

            var storedIds = await store.ReadAsync(doc => doc.Repos.Keys.ToList());
            Assert.Equal(new[] { "owner/main-repo" }, storedIds);
            Assert.Contains(summary.Skipped, s => s.Contains("duplicate id"));
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }
}
