using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Mcp;
using RepoAtlas.McpServer.Models;
using RepoAtlas.McpServer.Storage;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests.Mcp;

public class RepoResourcesTests
{
    [Fact]
    public async Task ListRepos_ReturnsJsonArrayOfSummaries()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            await store.MutateAsync(doc =>
                doc.Repos["owner/repo"] = new RepoEntry { Path = "p", Description = "desc", Tags = { "t1" } });
            var catalog = CatalogTestFactory.NewCatalog(store);

            var json = await RepoResources.ListRepos(catalog, NullLogger<RepoResources>.Instance);
            var summaries = JsonSerializer.Deserialize<RepoSummary[]>(json)!;

            Assert.Single(summaries);
            Assert.Equal("owner/repo", summaries[0].Id);
            Assert.Equal("desc", summaries[0].Description);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetRepoDetail_KnownId_ReturnsCuratedDataMergedWithLiveGitFactsAndReadme()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            await store.MutateAsync(doc =>
                doc.Repos["owner/repo"] = new RepoEntry { Path = "p", Description = "desc", Tags = { "t1" } });
            var facts = new GitFacts([new GitRemote("origin", "https://github.com/owner/repo.git")], "main", "abc123", false, null);
            var catalog = CatalogTestFactory.NewCatalog(store, readGitFacts: _ => facts, readReadme: _ => "# Hello");

            var json = await RepoResources.GetRepoDetail(catalog, NullLogger<RepoResources>.Instance, "owner/repo");
            var detail = JsonSerializer.Deserialize<RepoDetail>(json)!;

            Assert.Equal("owner/repo", detail.Id);
            Assert.Equal("desc", detail.Description);
            Assert.False(detail.Unreachable);
            Assert.Equal("main", detail.CurrentBranch);
            Assert.Equal("abc123", detail.HeadSha);
            Assert.Equal("# Hello", detail.ReadmeContent);
            Assert.Single(detail.Remotes);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task GetRepoDetail_UnknownId_ThrowsMcpException()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var store = CatalogTestFactory.NewStore(Path.Combine(tempDir.FullName, "data.yaml"));
            var catalog = CatalogTestFactory.NewCatalog(store);

            await Assert.ThrowsAsync<McpException>(() => RepoResources.GetRepoDetail(catalog, NullLogger<RepoResources>.Instance, "missing/repo"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
