using RepoAtlas.McpServer.Models;
using RepoAtlas.McpServer.Storage;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests.Storage;

public class YamlDataStoreTests
{
    private static string NewTempPath(out DirectoryInfo tempDir)
    {
        tempDir = Directory.CreateTempSubdirectory();
        return Path.Combine(tempDir.FullName, "data.yaml");
    }

    [Fact]
    public async Task ReadAsync_WhenFileDoesNotExist_ReturnsEmptyDocument()
    {
        var path = NewTempPath(out var tempDir);
        try
        {
            var store = CatalogTestFactory.NewStore(path);

            var repoCount = await store.ReadAsync(doc => doc.Repos.Count);
            var rootCount = await store.ReadAsync(doc => doc.Roots.Count);

            Assert.Equal(0, repoCount);
            Assert.Equal(0, rootCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MutateAsync_PersistsChangesAcrossNewInstances()
    {
        var path = NewTempPath(out var tempDir);
        try
        {
            var store = CatalogTestFactory.NewStore(path);
            await store.MutateAsync(doc =>
            {
                doc.Roots.Add(@"C:\code\github.com");
                doc.Repos["owner/repo"] = new RepoEntry
                {
                    Path = @"C:\code\github.com\owner\repo",
                    Description = "test repo",
                    Tags = { "tag1" }
                };
            });

            var reloaded = CatalogTestFactory.NewStore(path);
            var roots = await reloaded.ReadAsync(doc => doc.Roots);
            var repo = await reloaded.ReadAsync(doc => doc.Repos["owner/repo"]);

            Assert.Contains(@"C:\code\github.com", roots);
            Assert.Equal("test repo", repo.Description);
            Assert.Contains("tag1", repo.Tags);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Constructor_WhenFileIsMalformedYaml_ThrowsInvalidDataException()
    {
        var path = NewTempPath(out var tempDir);
        try
        {
            File.WriteAllText(path, "repos:\n\tinvalid-tab-indentation: true");

            Assert.Throws<InvalidDataException>(() => CatalogTestFactory.NewStore(path));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MutateAsync_SerializesConcurrentCallsWithoutLosingUpdates()
    {
        var path = NewTempPath(out var tempDir);
        try
        {
            var store = CatalogTestFactory.NewStore(path);

            var tasks = Enumerable.Range(0, 20).Select(i => store.MutateAsync(doc =>
            {
                doc.Repos[$"owner/repo-{i}"] = new RepoEntry { Path = $@"C:\repo-{i}" };
            }));

            await Task.WhenAll(tasks);

            var count = await store.ReadAsync(doc => doc.Repos.Count);
            Assert.Equal(20, count);

            var reloaded = CatalogTestFactory.NewStore(path);
            var reloadedCount = await reloaded.ReadAsync(doc => doc.Repos.Count);
            Assert.Equal(20, reloadedCount);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MutateAsync_AcrossTwoInstancesPointingAtTheSameFile_SerializesWritesWithoutLosingUpdates()
    {
        // Simulates two MCP server sessions (each its own YamlDataStore instance, so its own
        // in-process semaphore) sharing one data file — the scenario the cross-process file lock
        // exists to protect. Without it, interleaved read-mutate-write cycles across the two
        // instances would lose one side's updates.
        var path = NewTempPath(out var tempDir);
        try
        {
            var storeA = CatalogTestFactory.NewStore(path);
            var storeB = CatalogTestFactory.NewStore(path);

            var tasksA = Enumerable.Range(0, 10).Select(i => storeA.MutateAsync(doc =>
            {
                doc.Repos[$"owner/a-{i}"] = new RepoEntry { Path = $@"C:\a-{i}" };
            }));
            var tasksB = Enumerable.Range(0, 10).Select(i => storeB.MutateAsync(doc =>
            {
                doc.Repos[$"owner/b-{i}"] = new RepoEntry { Path = $@"C:\b-{i}" };
            }));

            await Task.WhenAll(tasksA.Concat(tasksB));

            var reloaded = CatalogTestFactory.NewStore(path);
            var count = await reloaded.ReadAsync(doc => doc.Repos.Count);
            Assert.Equal(20, count);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MutateAsync_WhenMutationThrows_LeavesFileUnchanged()
    {
        var path = NewTempPath(out var tempDir);
        try
        {
            var store = CatalogTestFactory.NewStore(path);
            await store.MutateAsync(doc => doc.Repos["owner/repo"] = new RepoEntry { Path = "p" });
            var before = File.ReadAllText(path);

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.MutateAsync(doc =>
            {
                doc.Repos["owner/repo"].Description = "should not persist";
                throw new InvalidOperationException("simulated failure");
            }));

            var after = File.ReadAllText(path);
            Assert.Equal(before, after);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task MutateAsync_WhenMutationThrows_LeavesInMemoryStateUnchangedToo()
    {
        var path = NewTempPath(out var tempDir);
        try
        {
            var store = CatalogTestFactory.NewStore(path);
            await store.MutateAsync(doc => doc.Repos["owner/repo"] = new RepoEntry { Path = "p", Description = "original" });

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.MutateAsync(doc =>
            {
                doc.Repos["owner/repo"].Description = "should not persist";
                throw new InvalidOperationException("simulated failure");
            }));

            // Read through the *same* store instance (not a reload from disk) — this is the part
            // the file-only check above can't see: the failed mutation must not have leaked into
            // the in-memory document either, since a later unrelated successful MutateAsync would
            // otherwise persist it.
            var description = await store.ReadAsync(doc => doc.Repos["owner/repo"].Description);
            Assert.Equal("original", description);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
