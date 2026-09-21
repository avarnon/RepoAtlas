using Microsoft.Extensions.Logging.Abstractions;

namespace RepoAtlas.McpServer.Tests;

public class ReadmeReaderTests
{
    private static readonly ReadmeReader Reader = new(NullLogger<ReadmeReader>.Instance);

    [Fact]
    public void Read_ReturnsContent_WhenReadmeExists()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "README.md"), "# Hello");

            var content = Reader.Read(tempDir.FullName);

            Assert.Equal("# Hello", content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_IsCaseInsensitiveToTheFileName()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "readme.md"), "lowercase");

            var content = Reader.Read(tempDir.FullName);

            Assert.Equal("lowercase", content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsNull_WhenNoReadmeExists()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var content = Reader.Read(tempDir.FullName);

            Assert.Null(content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsTruncatedContentWithANotice_WhenReadmeExceedsTheDisplayLimit()
    {
        const int limitBytes = 64 * 1024;
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var oversized = new string('a', limitBytes + 1);
            File.WriteAllText(Path.Combine(tempDir.FullName, "README.md"), oversized);

            var content = Reader.Read(tempDir.FullName);

            Assert.NotNull(content);
            Assert.StartsWith(new string('a', limitBytes), content);
            Assert.Contains("truncated", content);
            Assert.DoesNotContain(new string('a', limitBytes + 1), content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_TruncatesByBytesNotChars_WhenReadmeIsMultiByteUtf8AndExceedsTheDisplayLimit()
    {
        // A multi-byte-heavy README can be well under the byte cap in *character* count while
        // still exceeding it in *bytes* — MaxReadmeBytes is a byte limit (matching FileInfo.Length,
        // also bytes), so truncation must operate on the UTF-8 byte stream, not String.Length.
        const int limitBytes = 64 * 1024;
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            // 'é' is 2 bytes in UTF-8: 40,000 chars is 80,000 bytes on disk, comfortably over the
            // byte cap, while 40,000 is comfortably under it as a char count.
            var oversized = new string('é', 40_000);
            var path = Path.Combine(tempDir.FullName, "README.md");
            File.WriteAllText(path, oversized, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var writtenBytes = new FileInfo(path).Length;
            Assert.True(writtenBytes > limitBytes, "Test fixture must exceed the byte cap to exercise truncation.");

            var content = Reader.Read(tempDir.FullName);

            Assert.NotNull(content);
            Assert.Contains("truncated", content);
            Assert.True(content!.Length < oversized.Length,
                "The returned content must be genuinely shorter than the source — truncating by chars " +
                "instead of bytes can compare a small char count against a large byte limit and truncate nothing.");
            // No lone surrogate / broken multi-byte sequence at the truncation boundary.
            Assert.DoesNotContain('�', content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsFullContent_WhenReadmeIsAtExactlyTheDisplayLimit()
    {
        const int limitBytes = 64 * 1024;
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var atLimit = new string('a', limitBytes);
            File.WriteAllText(Path.Combine(tempDir.FullName, "README.md"), atLimit);

            var content = Reader.Read(tempDir.FullName);

            Assert.Equal(atLimit, content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Read_ReturnsContent_WhenReadmeIsASymlinkWithinTheRepo()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var realPath = Path.Combine(tempDir.FullName, "ACTUAL_README.md");
            File.WriteAllText(realPath, "real content");
            var linkPath = Path.Combine(tempDir.FullName, "README.md");

            CreateSymlinkOrSkip(linkPath, realPath);

            var content = Reader.Read(tempDir.FullName);

            Assert.Equal("real content", content);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Read_ReturnsContent_WhenRepoIsReachedViaSymlinkAndReadmeIsAPlainFile()
    {
        // The common case matching how RepoDiscoverer catalogs a repo only reachable through a
        // symlink (Scan_FindsRepoOnlyReachableThroughASymlink): repoPath is the symlink's path,
        // and README.md is an ordinary file, not itself a link.
        var storageDir = Directory.CreateTempSubdirectory();
        var scanRoot = Directory.CreateTempSubdirectory();
        try
        {
            var realRepoDir = Path.Combine(storageDir.FullName, "repo");
            Directory.CreateDirectory(realRepoDir);
            File.WriteAllText(Path.Combine(realRepoDir, "README.md"), "plain content");

            var linkedRepoPath = Path.Combine(scanRoot.FullName, "link-to-repo");
            CreateDirectorySymlinkOrSkip(linkedRepoPath, realRepoDir);

            var content = Reader.Read(linkedRepoPath);

            Assert.Equal("plain content", content);
        }
        finally
        {
            storageDir.Delete(recursive: true);
            scanRoot.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Read_ReturnsContent_WhenRepoIsReachedViaSymlinkAndReadmeIsAlsoASymlinkWithinIt()
    {
        // Mirrors how RepoDiscoverer catalogs a repo that's only reachable through a symlinked
        // directory: repoPath passed to Read() below is the *symlink's* path, not the real one.
        // A README.md that's itself a symlink to another file within that same real repo must
        // still be confined-check as "inside", even though its resolved target is expressed via
        // the real path rather than the symlink repoPath came in as.
        var storageDir = Directory.CreateTempSubdirectory();
        var scanRoot = Directory.CreateTempSubdirectory();
        try
        {
            var realRepoDir = Path.Combine(storageDir.FullName, "repo");
            Directory.CreateDirectory(realRepoDir);
            var realContentPath = Path.Combine(realRepoDir, "ACTUAL_README.md");
            File.WriteAllText(realContentPath, "content via nested link");

            var linkedRepoPath = Path.Combine(scanRoot.FullName, "link-to-repo");
            CreateDirectorySymlinkOrSkip(linkedRepoPath, realRepoDir);

            var readmeLinkPath = Path.Combine(realRepoDir, "README.md");
            CreateSymlinkOrSkip(readmeLinkPath, realContentPath);

            var content = Reader.Read(linkedRepoPath);

            Assert.Equal("content via nested link", content);
        }
        finally
        {
            storageDir.Delete(recursive: true);
            scanRoot.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Read_ReturnsNull_WhenReadmeIsASymlinkResolvingOutsideTheRepo()
    {
        var repoDir = Directory.CreateTempSubdirectory();
        var outsideDir = Directory.CreateTempSubdirectory();
        try
        {
            var secretPath = Path.Combine(outsideDir.FullName, "secret.txt");
            File.WriteAllText(secretPath, "top secret");
            var linkPath = Path.Combine(repoDir.FullName, "README.md");

            CreateSymlinkOrSkip(linkPath, secretPath);

            var content = Reader.Read(repoDir.FullName);

            Assert.Null(content);
        }
        finally
        {
            repoDir.Delete(recursive: true);
            outsideDir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Creates a symlink, reporting the test as skipped (instead of failing or silently passing)
    /// if the environment can't grant the privilege needed to do so — e.g. no Developer Mode on Windows.
    /// </summary>
    private static void CreateSymlinkOrSkip(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SkipException("Creating a symlink requires elevated privileges (or Developer Mode) on Windows.", ex);
        }
    }

    /// <summary>Directory-symlink counterpart to <see cref="CreateSymlinkOrSkip"/>.</summary>
    private static void CreateDirectorySymlinkOrSkip(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SkipException("Creating a symlink requires elevated privileges (or Developer Mode) on Windows.", ex);
        }
    }
}
