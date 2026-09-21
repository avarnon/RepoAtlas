using LibGit2Sharp;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Models;
using RepoAtlas.McpServer.Storage;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests;

public class RepoCatalogReadTests
{
    private static YamlDataStore NewStoreWithRepo(DirectoryInfo tempDir, string id, string path, bool unreachable = false)
    {
        var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
        store.MutateAsync(doc =>
        {
            doc.Repos[id] = new RepoEntry
            {
                Path = path,
                Description = "a repo",
                Tags = { "tag1" },
                Unreachable = unreachable,
            };
        }).GetAwaiter().GetResult();
        return store;
    }

    [Fact]
    public async Task ListReposAsync_ReturnsSummariesSortedById()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            await store.MutateAsync(doc =>
            {
                doc.Repos["b/repo"] = new RepoEntry { Path = "p2", Description = "second" };
                doc.Repos["a/repo"] = new RepoEntry { Path = "p1", Description = "first" };
            });

            var catalog = CatalogTestFactory.NewCatalog(store);

            var summaries = await catalog.ListReposAsync();

            Assert.Equal(new[] { "a/repo", "b/repo" }, summaries.Select(s => s.Id));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetRepoDetailAsync_MergesCuratedDataWithLiveGitFactsAndReadme()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = NewStoreWithRepo(tempDir, "owner/repo", @"C:\fake\path");
            var expectedFacts = new GitFacts(
                [new GitRemote("origin", "https://github.com/owner/repo.git")],
                "main", "abc123", false, null);

            var catalog = CatalogTestFactory.NewCatalog(store,
                readGitFacts: path => path == @"C:\fake\path" ? expectedFacts : throw new InvalidOperationException("wrong path"),
                readReadme: path => path == @"C:\fake\path" ? "# Hello" : throw new InvalidOperationException("wrong path"));

            var detail = await catalog.GetRepoDetailAsync("owner/repo");

            Assert.Equal("a repo", detail.Description);
            Assert.Equal("main", detail.CurrentBranch);
            Assert.Equal("abc123", detail.HeadSha);
            Assert.Equal("# Hello", detail.ReadmeContent);
            Assert.False(detail.Unreachable);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetRepoDetailAsync_UnknownId_ThrowsRepoNotFoundException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var catalog = CatalogTestFactory.NewCatalog(store);

            await Assert.ThrowsAsync<RepoNotFoundException>(() => catalog.GetRepoDetailAsync("missing/repo"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ListReposAsync_ReturnedTagsAreIndependentOfLaterStoreMutations()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = NewStoreWithRepo(tempDir, "owner/repo", @"C:\fake\path");
            var catalog = CatalogTestFactory.NewCatalog(store);

            var summaries = await catalog.ListReposAsync();
            var returnedTags = summaries.Single().Tags;

            await store.MutateAsync(doc => doc.Repos["owner/repo"].Tags.Add("added-later"));

            Assert.Equal(["tag1"], returnedTags);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetRepoDetailAsync_ReturnedTagsAndDependenciesAreIndependentOfLaterStoreMutations()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            await store.MutateAsync(doc =>
            {
                doc.Repos["owner/repo"] = new RepoEntry
                {
                    Path = @"C:\gone",
                    Description = "a repo",
                    Tags = { "tag1" },
                    DependsOn = { new DependencyLink { Id = "owner/other", Note = "original" } },
                    Unreachable = true,
                };
            });
            var catalog = CatalogTestFactory.NewCatalog(store);

            var detail = await catalog.GetRepoDetailAsync("owner/repo");

            await store.MutateAsync(doc =>
            {
                doc.Repos["owner/repo"].Tags.Add("added-later");
                doc.Repos["owner/repo"].DependsOn[0].Note = "mutated";
                doc.Repos["owner/repo"].DependsOn.Add(new DependencyLink { Id = "owner/third" });
            });

            Assert.Equal(["tag1"], detail.Tags);
            Assert.Single(detail.DependsOn);
            Assert.Equal("original", detail.DependsOn[0].Note);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(typeof(RepositoryNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public async Task GetRepoDetailAsync_RepoFolderVanishedSinceLastRescan_ReportsUnreachableInsteadOfThrowingRaw(Type exceptionType)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            // Unreachable is only refreshed by Rescan, so a repo whose folder disappeared since
            // the last rescan is still flagged reachable (Unreachable: false) when this runs.
            var store = NewStoreWithRepo(tempDir, "owner/repo", @"C:\gone", unreachable: false);
            var exception = (Exception)Activator.CreateInstance(exceptionType, "simulated: gone")!;
            var catalog = CatalogTestFactory.NewCatalog(store,
                readGitFacts: _ => throw exception);

            var detail = await catalog.GetRepoDetailAsync("owner/repo");

            Assert.True(detail.Unreachable);
            Assert.Null(detail.ReadmeContent);
            Assert.Null(detail.CurrentBranch);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetRepoDetailAsync_UnreachableRepo_SkipsGitAndReadmeReads()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = NewStoreWithRepo(tempDir, "owner/repo", @"C:\gone", unreachable: true);
            var catalog = CatalogTestFactory.NewCatalog(store);

            var detail = await catalog.GetRepoDetailAsync("owner/repo");

            Assert.True(detail.Unreachable);
            Assert.Null(detail.ReadmeContent);
            Assert.Null(detail.CurrentBranch);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
