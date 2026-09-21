namespace RepoAtlas.McpServer.Tests;

public class AllowedRootBasesParserTests
{
    [Fact]
    public void Parse_SplitsOnThePlatformPathSeparator_TrimmingEachEntry()
    {
        // Deliberately not Windows drive-letter paths (e.g. "C:\code"): on Unix, Path.PathSeparator
        // is ':', which a drive letter's colon would itself collide with, splitting mid-path.
        var raw = $" /code {Path.PathSeparator} /other ";

        var result = AllowedRootBasesParser.Parse(raw);

        Assert.Equal(new[] { "/code", "/other" }, result);
    }

    [Fact]
    public void Parse_RemovesEmptyEntries_FromConsecutiveOrTrailingSeparators()
    {
        var raw = $"/code{Path.PathSeparator}{Path.PathSeparator}/other{Path.PathSeparator}";

        var result = AllowedRootBasesParser.Parse(raw);

        Assert.Equal(new[] { "/code", "/other" }, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NullEmptyOrWhitespace_ReturnsAnEmptyList(string? raw)
    {
        var result = AllowedRootBasesParser.Parse(raw);

        Assert.Empty(result);
    }
}
