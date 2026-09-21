using RepoAtlas.McpServer.Models;
using RepoAtlas.McpServer.Storage;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests;

public class RepoCatalogWriteTests
{
    private static async Task<(RepoCatalog Catalog, YamlDataStore Store, string DataPath)> NewCatalog(
        DirectoryInfo tempDir, CancellationToken cancellationToken, params string[] repoIds) =>
        await NewCatalog(tempDir, cancellationToken, allowedRootBases: null, repoIds: repoIds);

    private static async Task<(RepoCatalog Catalog, YamlDataStore Store, string DataPath)> NewCatalog(
        DirectoryInfo tempDir, CancellationToken cancellationToken, IReadOnlyList<string>? allowedRootBases, params string[] repoIds)
    {
        var dataPath = Path.Combine(tempDir.FullName, "data.yaml");
        var store = CatalogTestFactory.NewStore(dataPath);
        await store.MutateAsync(doc =>
        {
            foreach (var id in repoIds)
            {
                doc.Repos[id] = new RepoEntry { Path = $@"C:\{id}" };
            }
        }, cancellationToken);

        var catalog = CatalogTestFactory.NewCatalog(store, allowedRootBases: allowedRootBases);

        return (catalog, store, dataPath);
    }

    [Fact]
    public async Task UpdateDescriptionAsync_ReplacesDescription()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None, "owner/repo");

            await catalog.UpdateDescriptionAsync("owner/repo", "new description");

            var description = await store.ReadAsync(doc => doc.Repos["owner/repo"].Description);
            Assert.Equal("new description", description);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UpdateDescriptionAsync_UnknownId_ThrowsAndLeavesFileUnchanged()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _, dataPath) = await NewCatalog(tempDir, CancellationToken.None, "owner/repo");
            var before = File.ReadAllText(dataPath);

            await Assert.ThrowsAsync<RepoNotFoundException>(() => catalog.UpdateDescriptionAsync("missing/repo", "x"));

            Assert.Equal(before, File.ReadAllText(dataPath));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddTagAsync_IsIdempotent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None, "owner/repo");

            await catalog.AddTagAsync("owner/repo", "mcp");
            await catalog.AddTagAsync("owner/repo", "mcp");

            var tags = await store.ReadAsync(doc => doc.Repos["owner/repo"].Tags);
            Assert.Equal(new[] { "mcp" }, tags);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RemoveTagAsync_OnAbsentTag_IsANoOp()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None, "owner/repo");

            await catalog.RemoveTagAsync("owner/repo", "never-added");

            var tags = await store.ReadAsync(doc => doc.Repos["owner/repo"].Tags);
            Assert.Empty(tags);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddDependencyAsync_SelfDependency_ThrowsInvalidDependencyException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _, _) = await NewCatalog(tempDir, CancellationToken.None, "owner/repo");

            await Assert.ThrowsAsync<InvalidDependencyException>(() =>
                catalog.AddDependencyAsync("owner/repo", "owner/repo", null));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("missing/from", "owner/to")]
    [InlineData("owner/from", "missing/to")]
    public async Task AddDependencyAsync_UnknownId_ThrowsRepoNotFoundException(string fromId, string toId)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _, _) = await NewCatalog(tempDir, CancellationToken.None, "owner/from", "owner/to");

            await Assert.ThrowsAsync<RepoNotFoundException>(() =>
                catalog.AddDependencyAsync(fromId, toId, null));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddDependencyAsync_CalledTwiceForSamePair_UpdatesNoteInstadOfDuplicating()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None, "owner/from", "owner/to");

            await catalog.AddDependencyAsync("owner/from", "owner/to", "first note");
            await catalog.AddDependencyAsync("owner/from", "owner/to", "second note");

            var links = await store.ReadAsync(doc => doc.Repos["owner/from"].DependsOn);
            Assert.Single(links);
            Assert.Equal("second note", links[0].Note);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RemoveDependencyAsync_RemovesExistingLink_AndIsANoOpWhenAbsent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None, "owner/from", "owner/to");
            await catalog.AddDependencyAsync("owner/from", "owner/to", null);

            await catalog.RemoveDependencyAsync("owner/from", "owner/to");
            await catalog.RemoveDependencyAsync("owner/from", "owner/to");

            var links = await store.ReadAsync(doc => doc.Repos["owner/from"].DependsOn);
            Assert.Empty(links);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddRootAsync_IsIdempotent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None);
            var root = Path.Combine(tempDir.FullName, "code");

            await catalog.AddRootAsync(root);
            await catalog.AddRootAsync(root);

            var roots = await store.ReadAsync(doc => doc.Roots);
            Assert.Equal(new[] { Path.GetFullPath(root) }, roots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddRootAsync_NormalizesToFullPath()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None);
            var relative = Path.Combine(tempDir.FullName, "a", "..", "code");

            await catalog.AddRootAsync(relative);

            var roots = await store.ReadAsync(doc => doc.Roots);
            Assert.Equal(new[] { Path.GetFullPath(relative) }, roots);
            Assert.DoesNotContain("..", roots[0]);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddRootAsync_RootIsAFilesystemRoot_ThrowsRootNotAllowedExceptionAndLeavesFileUnchanged()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _, dataPath) = await NewCatalog(tempDir, CancellationToken.None);
            var filesystemRoot = Path.GetPathRoot(tempDir.FullName)!;
            var before = File.ReadAllText(dataPath);

            await Assert.ThrowsAsync<RootNotAllowedException>(() => catalog.AddRootAsync(filesystemRoot));

            Assert.Equal(before, File.ReadAllText(dataPath));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddRootAsync_OutsideConfiguredAllowedBases_ThrowsRootNotAllowedException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var allowedBase = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _, _) = await NewCatalog(tempDir, CancellationToken.None, allowedRootBases: [allowedBase.FullName]);
            var disallowedRoot = Path.Combine(tempDir.FullName, "code");

            await Assert.ThrowsAsync<RootNotAllowedException>(() => catalog.AddRootAsync(disallowedRoot));
        }
        finally
        {
            tempDir.Delete(recursive: true);
            allowedBase.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddRootAsync_UnderAConfiguredAllowedBase_Succeeds()
    {
        var allowedBase = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(allowedBase, CancellationToken.None, allowedRootBases: [allowedBase.FullName]);
            var allowedRoot = Path.Combine(allowedBase.FullName, "code");

            await catalog.AddRootAsync(allowedRoot);

            var roots = await store.ReadAsync(doc => doc.Roots);
            Assert.Equal(new[] { Path.GetFullPath(allowedRoot) }, roots);
        }
        finally
        {
            allowedBase.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RemoveRootAsync_RemovesExistingRoot_AndIsANoOpWhenAbsent()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store, _) = await NewCatalog(tempDir, CancellationToken.None);
            var root = Path.Combine(tempDir.FullName, "code");
            await catalog.AddRootAsync(root);

            await catalog.RemoveRootAsync(root);
            await catalog.RemoveRootAsync(root);

            var roots = await store.ReadAsync(doc => doc.Roots);
            Assert.Empty(roots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ListRootsAsync_ReturnsConfiguredRootsSorted()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _, _) = await NewCatalog(tempDir, CancellationToken.None);
            var rootB = Path.Combine(tempDir.FullName, "b");
            var rootA = Path.Combine(tempDir.FullName, "a");
            await catalog.AddRootAsync(rootB);
            await catalog.AddRootAsync(rootA);

            var roots = await catalog.ListRootsAsync();

            Assert.Equal(new[] { Path.GetFullPath(rootA), Path.GetFullPath(rootB) }, roots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
