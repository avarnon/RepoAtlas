using Microsoft.Extensions.Logging.Abstractions;
using RepoAtlas.McpServer.Storage;

namespace RepoAtlas.McpServer.Tests.Storage;

public class AtomicFileWriterTests
{
    private static readonly AtomicFileWriter Writer = new(NullLogger<AtomicFileWriter>.Instance);

    [Fact]
    public async Task WriteAllText_WritesContentAndCreatesMissingDirectories()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var targetPath = Path.Combine(tempDir.FullName, "nested", "data.yaml");

            await Writer.WriteAllTextAsync(targetPath, "hello: world");

            Assert.Equal("hello: world", File.ReadAllText(targetPath));
            Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(targetPath)!), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllText_OverwritesExistingFile()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var targetPath = Path.Combine(tempDir.FullName, "data.yaml");
            File.WriteAllText(targetPath, "old content");

            await Writer.WriteAllTextAsync(targetPath, "new content");

            Assert.Equal("new content", File.ReadAllText(targetPath));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllText_RetriesTheRenameWhenDestinationIsTransientlyLocked()
    {
        // Real Windows share-mode locking: exercises the retry loop against actual OS behavior,
        // but only on Windows — on Linux/macOS, File.Move (rename(2)) succeeds even with the
        // destination open for read, so this wouldn't exercise the retry there at all. The
        // platform-independent regression test below is what actually proves the retry runs.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var targetPath = Path.Combine(tempDir.FullName, "data.yaml");
            File.WriteAllText(targetPath, "old content");

            using var blocker = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            _ = Task.Run(async () =>
            {
                await Task.Delay(30);
                blocker.Dispose();
            });

            await Writer.WriteAllTextAsync(targetPath, "new content");

            Assert.Equal("new content", File.ReadAllText(targetPath));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllText_RetriesTheRenameWhenTheMoveThrowsTransiently()
    {
        // Deterministic, platform-independent regression test for the retry loop itself, using the
        // internal move seam instead of relying on real OS file-locking semantics (which don't
        // reproduce the same way across Windows and Linux/macOS — see the test above).
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var targetPath = Path.Combine(tempDir.FullName, "data.yaml");
            var attempts = 0;
            var writer = new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance, (source, destination) =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new IOException("simulated transient lock");
                }
                File.Move(source, destination, overwrite: true);
            });

            await writer.WriteAllTextAsync(targetPath, "new content");

            Assert.Equal(3, attempts);
            Assert.Equal("new content", File.ReadAllText(targetPath));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllText_WhenTheMoveKeepsFailing_GivesUpAfterFourAttemptsAndThrows()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var targetPath = Path.Combine(tempDir.FullName, "data.yaml");
            var attempts = 0;
            var writer = new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance, (_, _) =>
            {
                attempts++;
                throw new IOException("simulated permanent lock");
            });

            await Assert.ThrowsAsync<IOException>(() => writer.WriteAllTextAsync(targetPath, "new content"));

            Assert.Equal(4, attempts);
            Assert.DoesNotContain(Directory.EnumerateFiles(tempDir.FullName), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteAllText_WhenTargetIsADirectory_ThrowsAndLeavesItUntouched()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var targetPath = Path.Combine(tempDir.FullName, "data");
            Directory.CreateDirectory(targetPath);

            var ex = await Record.ExceptionAsync(() => Writer.WriteAllTextAsync(targetPath, "new content"));

            Assert.True(ex is IOException or UnauthorizedAccessException,
                $"Expected an IOException or UnauthorizedAccessException (a file can't be moved onto an existing directory), but got {ex?.GetType().FullName}.");
            Assert.True(Directory.Exists(targetPath));
            Assert.DoesNotContain(Directory.EnumerateFiles(tempDir.FullName), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
