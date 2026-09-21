using Microsoft.Extensions.Logging.Abstractions;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests.Git;

public class GitFactsReaderTests
{
    private static readonly GitFactsReader Reader = new(NullLogger<GitFactsReader>.Instance);

    [Fact]
    public void Read_WithOriginOnly_ReportsNoLineage()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes: [("origin", "https://github.com/avarnon/repo.git")]);

            var facts = Reader.Read(repoPath);

            Assert.Single(facts.Remotes);
            Assert.False(facts.IsFork);
            Assert.Null(facts.UpstreamUrl);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_WithNamedUpstreamRemote_ReportsLineage()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes:
                [
                    ("origin", "https://github.com/avarnon/mcp-for-beginners.git"),
                    ("upstream", "https://github.com/microsoft/mcp-for-beginners.git"),
                ]);

            var facts = Reader.Read(repoPath);

            Assert.True(facts.IsFork);
            Assert.Equal("https://github.com/microsoft/mcp-for-beginners.git", facts.UpstreamUrl);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_WithDifferingOwnerRemoteNotNamedUpstream_ReportsLineage()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes:
                [
                    ("origin", "https://github.com/avarnon/mcp-for-beginners.git"),
                    ("msft", "https://github.com/microsoft/mcp-for-beginners.git"),
                ]);

            var facts = Reader.Read(repoPath);

            Assert.True(facts.IsFork);
            Assert.Equal("https://github.com/microsoft/mcp-for-beginners.git", facts.UpstreamUrl);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_WithCommit_ReportsBranchAndHeadSha()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo", withCommit: true);

            var facts = Reader.Read(repoPath);

            Assert.False(string.IsNullOrWhiteSpace(facts.CurrentBranch));
            Assert.False(string.IsNullOrWhiteSpace(facts.HeadSha));
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_WithNoCommits_ReportsNullHeadSha()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo");

            var facts = Reader.Read(repoPath);

            Assert.Null(facts.HeadSha);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_RemoteUrlWithEmbeddedCredentials_StripsUserInfo()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes: [("origin", "https://user:ghp_xxxxxxxx@github.com/owner/repo.git")]);

            var facts = Reader.Read(repoPath);

            Assert.Equal("https://github.com/owner/repo.git", facts.Remotes.Single().Url);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_UpstreamUrlWithEmbeddedCredentials_StripsUserInfo()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes:
                [
                    ("origin", "https://github.com/avarnon/repo.git"),
                    ("upstream", "https://user:ghp_xxxxxxxx@github.com/microsoft/repo.git"),
                ]);

            var facts = Reader.Read(repoPath);

            Assert.Equal("https://github.com/microsoft/repo.git", facts.UpstreamUrl);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_NormalRemoteUrl_IsUnaffected()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes: [("origin", "https://github.com/owner/repo.git")]);

            var facts = Reader.Read(repoPath);

            Assert.Equal("https://github.com/owner/repo.git", facts.Remotes.Single().Url);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Read_ScpStyleRemoteUrl_IsUnaffected()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes: [("origin", "git@github.com:owner/repo.git")]);

            var facts = Reader.Read(repoPath);

            Assert.Equal("git@github.com:owner/repo.git", facts.Remotes.Single().Url);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }
}
