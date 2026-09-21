using RepoAtlas.McpServer.Discovery;

namespace RepoAtlas.McpServer.Tests.Discovery;

public class RepoIdDeriverTests
{
    [Theory]
    [InlineData("https://github.com/microsoft/mcp-for-beginners.git", "microsoft/mcp-for-beginners")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("git@github.com:avarnon/mcp-for-beginners.git", "avarnon/mcp-for-beginners")]
    [InlineData("ssh://git@github.com/owner/repo.git", "owner/repo")]
    public void TryParseOwnerRepo_ParsesKnownUrlShapes(string url, string expected)
    {
        Assert.Equal(expected, RepoIdDeriver.TryParseOwnerRepo(url));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("https://github.com/")]
    [InlineData("")]
    public void TryParseOwnerRepo_ReturnsNullForUnparseableInput(string url)
    {
        Assert.Null(RepoIdDeriver.TryParseOwnerRepo(url));
    }

    [Fact]
    public void DeriveId_UsesOwnerRepoWhenRemoteUrlParses()
    {
        var id = RepoIdDeriver.DeriveId("https://github.com/owner/repo.git", "folder-name");

        Assert.Equal("owner/repo", id);
    }

    [Fact]
    public void DeriveId_FallsBackToFolderNameWhenNoRemote()
    {
        var id = RepoIdDeriver.DeriveId(null, "my-local-repo");

        Assert.Equal("my-local-repo", id);
    }

    [Fact]
    public void DeriveId_FallsBackToFolderNameWhenRemoteUnparseable()
    {
        var id = RepoIdDeriver.DeriveId("not a url", "my-local-repo");

        Assert.Equal("my-local-repo", id);
    }
}
