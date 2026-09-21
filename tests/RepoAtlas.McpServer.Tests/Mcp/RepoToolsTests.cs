using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using RepoAtlas.McpServer.Discovery;
using RepoAtlas.McpServer.Mcp;
using RepoAtlas.McpServer.Models;
using RepoAtlas.McpServer.Storage;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests.Mcp;

public class RepoToolsTests
{
    private static (RepoCatalog Catalog, YamlDataStore Store) NewCatalog(DirectoryInfo tempDir, params string[] repoIds)
    {
        var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
        store.MutateAsync(doc =>
        {
            foreach (var id in repoIds)
            {
                doc.Repos[id] = new RepoEntry { Path = $@"C:\{id}" };
            }
        }).GetAwaiter().GetResult();

        var catalog = CatalogTestFactory.NewCatalog(store);

        return (catalog, store);
    }

    [Fact]
    public async Task UpdateDescription_UnknownId_ThrowsMcpException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _) = NewCatalog(tempDir);

            await Assert.ThrowsAsync<McpException>(() =>
                RepoTools.UpdateDescription(catalog, NullLogger<RepoTools>.Instance, "missing/repo", "x"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task UpdateDescription_KnownId_UpdatesAndReturnsConfirmation()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store) = NewCatalog(tempDir, "owner/repo");

            var result = await RepoTools.UpdateDescription(catalog, NullLogger<RepoTools>.Instance, "owner/repo", "new text");

            Assert.Contains("owner/repo", result);
            var description = await store.ReadAsync(doc => doc.Repos["owner/repo"].Description);
            Assert.Equal("new text", description);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddTag_AddsTheTag()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store) = NewCatalog(tempDir, "owner/repo");

            var result = await RepoTools.AddTag(catalog, NullLogger<RepoTools>.Instance, "owner/repo", "mcp");

            Assert.Contains("mcp", result);
            var tags = await store.ReadAsync(doc => doc.Repos["owner/repo"].Tags);
            Assert.Equal(new[] { "mcp" }, tags);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RemoveTag_UnknownId_ThrowsMcpException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _) = NewCatalog(tempDir);

            await Assert.ThrowsAsync<McpException>(() =>
                RepoTools.RemoveTag(catalog, NullLogger<RepoTools>.Instance, "missing/repo", "mcp"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RemoveDependency_RemovesAPreviouslyAddedDependency()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store) = NewCatalog(tempDir, "owner/from", "owner/to");
            await RepoTools.AddDependency(catalog, NullLogger<RepoTools>.Instance, "owner/from", "owner/to");

            var result = await RepoTools.RemoveDependency(catalog, NullLogger<RepoTools>.Instance, "owner/from", "owner/to");

            Assert.Contains("owner/from", result);
            var links = await store.ReadAsync(doc => doc.Repos["owner/from"].DependsOn);
            Assert.Empty(links);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddRoot_RootNotAllowed_ThrowsMcpException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var allowedBase = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var catalog = CatalogTestFactory.NewCatalog(store, allowedRootBases: [allowedBase.FullName]);
            var disallowedRoot = Path.Combine(tempDir.FullName, "code");

            await Assert.ThrowsAsync<McpException>(() =>
                RepoTools.AddRoot(catalog, NullLogger<RepoTools>.Instance, disallowedRoot));
        }
        finally
        {
            tempDir.Delete(recursive: true);
            allowedBase.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddDependency_SelfDependency_ThrowsMcpException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _) = NewCatalog(tempDir, "owner/repo");

            await Assert.ThrowsAsync<McpException>(() =>
                RepoTools.AddDependency(catalog, NullLogger<RepoTools>.Instance, "owner/repo", "owner/repo", null));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AddRoot_ThenListRoots_ReturnsTheAddedRoot()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, _) = NewCatalog(tempDir);
            var root = Path.Combine(tempDir.FullName, "code");

            await RepoTools.AddRoot(catalog, NullLogger<RepoTools>.Instance, root);
            var result = await RepoTools.ListRoots(catalog, NullLogger<RepoTools>.Instance);

            var roots = JsonSerializer.Deserialize<List<string>>(result);
            Assert.Equal([Path.GetFullPath(root)], roots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RemoveRoot_RemovesAPreviouslyAddedRoot()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var (catalog, store) = NewCatalog(tempDir);
            var root = Path.Combine(tempDir.FullName, "code");
            await RepoTools.AddRoot(catalog, NullLogger<RepoTools>.Instance, root);

            await RepoTools.RemoveRoot(catalog, NullLogger<RepoTools>.Instance, root);

            var roots = await store.ReadAsync(doc => doc.Roots);
            Assert.Empty(roots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Rescan_ReturnsJsonSummary()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var dataPath = Path.Combine(tempDir.FullName, "data.yaml");
            var store = CatalogTestFactory.NewStore(dataPath);
            var scanResult = new ScanResult(
                [new DiscoveredRepo("owner/repo", @"C:\owner\repo")],
                Array.Empty<string>(),
                Array.Empty<SkippedPath>());
            var catalog = CatalogTestFactory.NewCatalog(store, scan: _ => scanResult);

            var result = await RepoTools.Rescan(catalog, NullLogger<RepoTools>.Instance);

            Assert.Contains("owner/repo", result);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
