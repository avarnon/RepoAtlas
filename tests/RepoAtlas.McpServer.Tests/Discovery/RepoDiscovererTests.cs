using Microsoft.Extensions.Logging.Abstractions;
using RepoAtlas.McpServer.Discovery;
using RepoAtlas.McpServer.Git;
using RepoAtlas.McpServer.Tests.TestHelpers;

namespace RepoAtlas.McpServer.Tests.Discovery;

public class RepoDiscovererTests
{
    private sealed class ThrowingGitFactsReader(Exception toThrow) : IGitFactsReader
    {
        public IReadOnlyList<GitRemote> GetRemotes(string repoPath) => throw toThrow;

        public GitFacts Read(string repoPath) => throw new NotSupportedException("not used");
    }

    private static RepoDiscoverer NewDiscoverer(int? maxDepth = null) =>
        maxDepth is null
            ? new(new GitFactsReader(NullLogger<GitFactsReader>.Instance), NullLogger<RepoDiscoverer>.Instance)
            : new(new GitFactsReader(NullLogger<GitFactsReader>.Instance), NullLogger<RepoDiscoverer>.Instance, maxDepth.Value);

    private static RepoDiscoverer NewDiscoverer(IGitFactsReader gitFactsReader) =>
        new(gitFactsReader, NullLogger<RepoDiscoverer>.Instance);

    [Fact]
    public void Scan_FindsRepoDirectlyUnderRootWithDerivedId()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes: [("origin", "https://github.com/owner/repo.git")]);

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Single(result.Repos);
            Assert.Equal("owner/repo", result.Repos[0].Id);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_FindsNestedRepoAtArbitraryDepth()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            TempGitRepoFixture.CreateRepo(tempDir.FullName, Path.Combine("github.com", "avarnon", "repo"),
                remotes: [("origin", "https://github.com/avarnon/repo.git")]);

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Single(result.Repos);
            Assert.Equal("avarnon/repo", result.Repos[0].Id);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_UsesFolderNameWhenNoOriginRemote()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            TempGitRepoFixture.CreateRepo(tempDir.FullName, "local-only-repo");

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Single(result.Repos);
            Assert.Equal("local-only-repo", result.Repos[0].Id);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_DoesNotDescendIntoARepoItAlreadyFound()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var repoPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo");
            // A genuinely valid nested repo — if the scanner wrongly descended into
            // the outer repo, this would be found too and fail Assert.Single below.
            TempGitRepoFixture.CreateRepo(repoPath, "vendored");

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Single(result.Repos);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Scan_SkipsRepoWhenReadingRemotesThrowsAnIOOrAccessException_InsteadOfLettingTheScanFail(Type exceptionType)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo");
            var thrown = (Exception)Activator.CreateInstance(exceptionType, "simulated")!;

            var result = NewDiscoverer(new ThrowingGitFactsReader(thrown)).Scan([tempDir.FullName]);

            Assert.Empty(result.Repos);
            Assert.Contains(result.Skipped, s => s.Path == Path.Combine(tempDir.FullName, "repo"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_SkipsInvalidGitDirectoryWithoutThrowing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var brokenRepoDir = Path.Combine(tempDir.FullName, "broken");
            Directory.CreateDirectory(Path.Combine(brokenRepoDir, ".git"));

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Empty(result.Repos);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_SkipsInvalidGitDirectory_ReportsItInSkippedInsteadOfVanishingSilently()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var brokenRepoDir = Path.Combine(tempDir.FullName, "broken");
            Directory.CreateDirectory(Path.Combine(brokenRepoDir, ".git"));

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Empty(result.Repos);
            Assert.Contains(result.Skipped, s => s.Path == brokenRepoDir);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Scan_SkipsUnreadableDirectory_ReportsItInSkippedInsteadOfVanishingSilently()
    {
        // chmod-based permission denial only applies on Unix; there's no equally simple,
        // reliably-restorable way to deny self-access on Windows in a unit test. Skip.If (rather
        // than a plain `if (...) return;`) is what makes this report as skipped instead of
        // silently passing, but the platform-compat analyzer only recognizes the latter as a
        // guard for the SetUnixFileMode calls below — hence the pragmas.
        Skip.If(OperatingSystem.IsWindows(), "chmod-based permission denial only applies on Unix.");

        var tempDir = Directory.CreateTempSubdirectory();
        var lockedDir = Path.Combine(tempDir.FullName, "locked");
        Directory.CreateDirectory(lockedDir);
        Directory.CreateDirectory(Path.Combine(lockedDir, "should-not-be-seen"));

        try
        {
#pragma warning disable CA1416 // unreachable on Windows: guarded by Skip.If(OperatingSystem.IsWindows()) above
            File.SetUnixFileMode(lockedDir, UnixFileMode.None);
#pragma warning restore CA1416

            var enforced = false;
            try
            {
                _ = Directory.EnumerateDirectories(lockedDir).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                enforced = true;
            }

            Skip.IfNot(enforced, "Running with elevated privileges (e.g. root), which bypasses the restriction.");

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Empty(result.Repos);
            Assert.Contains(result.Skipped, s => s.Path == lockedDir);
        }
        finally
        {
#pragma warning disable CA1416 // unreachable on Windows: guarded by Skip.If(OperatingSystem.IsWindows()) above
            File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_ExceedingMaxDepth_StopsRecursingAndReportsSkip()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var deepest = tempDir.FullName;
            for (var i = 0; i < 4; i++)
            {
                deepest = Path.Combine(deepest, $"d{i}");
            }
            TempGitRepoFixture.CreateRepo(deepest, "repo",
                remotes: [("origin", "https://github.com/owner/too-deep.git")]);

            var result = NewDiscoverer(maxDepth: 2).Scan([tempDir.FullName]);

            Assert.Empty(result.Repos);
            Assert.Contains(result.Skipped, s => s.Reason.Contains("max scan depth"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Scan_FindsRepoOnlyReachableThroughASymlink()
    {
        var storageDir = Directory.CreateTempSubdirectory();
        var scanRoot = Directory.CreateTempSubdirectory();
        try
        {
            // The repo lives outside the scan root entirely; a symlink under the root is the
            // only path that reaches it. If the fix for cycle-avoidance (below) also refused to
            // check a symlinked directory for .git before deciding whether to descend, this
            // repo would vanish from the catalog rather than just becoming unreachable-via-cycle.
            var repoPath = TempGitRepoFixture.CreateRepo(storageDir.FullName, "repo",
                remotes: [("origin", "https://github.com/owner/repo.git")]);
            var linkPath = Path.Combine(scanRoot.FullName, "link-to-repo");

            CreateSymlinkOrSkip(linkPath, repoPath);

            var result = NewDiscoverer().Scan([scanRoot.FullName]);

            Assert.Single(result.Repos);
            Assert.Equal("owner/repo", result.Repos[0].Id);
            Assert.Equal(linkPath, result.Repos[0].Path);
        }
        finally
        {
            scanRoot.Delete(recursive: true);
            storageDir.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Scan_DoesNotDescendThroughASymlinkedDirectory_AvoidingPotentialCycles()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            // "real" is not itself a repo — only descending into it would find "nested-repo".
            var realDir = Path.Combine(tempDir.FullName, "real");
            Directory.CreateDirectory(realDir);
            TempGitRepoFixture.CreateRepo(realDir, "nested-repo",
                remotes: [("origin", "https://github.com/owner/repo.git")]);
            var linkPath = Path.Combine(tempDir.FullName, "link-to-real");

            CreateSymlinkOrSkip(linkPath, realDir);

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            // Found once, via the real path — not a second time via the link, and the link
            // wasn't followed into an infinite cycle.
            Assert.Single(result.Repos);
            Assert.Equal(Path.Combine(realDir, "nested-repo"), result.Repos[0].Path);
            Assert.Contains(result.Skipped, s => s.Path == linkPath);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_FindsABareRepository()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var barePath = Path.Combine(tempDir.FullName, "bare-repo.git");
            LibGit2Sharp.Repository.Init(barePath, isBare: true);
            using (var bareRepo = new LibGit2Sharp.Repository(barePath))
            {
                bareRepo.Network.Remotes.Add("origin", "https://github.com/owner/bare-repo.git");
            }

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Single(result.Repos);
            Assert.Equal("owner/bare-repo", result.Repos[0].Id);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void Scan_RecognizesALinkedWorktree_InsteadOfLosingItOrRecursingIntoIt()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var mainPath = TempGitRepoFixture.CreateRepo(tempDir.FullName, "main-repo",
                remotes: [("origin", "https://github.com/owner/main-repo.git")], withCommit: true);
            var worktreePath = Path.Combine(tempDir.FullName, "main-repo-worktree");

            using (var mainRepo = new LibGit2Sharp.Repository(mainPath))
            {
                mainRepo.Worktrees.Add("main-repo-worktree", worktreePath, isLocked: false);
            }

            // The worktree's ".git" is a *file* (pointing back at the main repo's git dir), not a
            // directory — without the fix, the scanner wouldn't recognize it as a repo at all, and
            // would instead recurse into it as a plain directory. This is discoverer-level: both
            // are found here, but since a worktree shares its main repo's origin remote, they
            // derive the same catalog id and RepoCatalog.RescanAsync's existing duplicate-id
            // handling then keeps only one (see RepoCatalogRescanTests for that end-to-end behavior).
            Assert.True(File.Exists(Path.Combine(worktreePath, ".git")));

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Equal(2, result.Repos.Count);
            Assert.Contains(result.Repos, r => r.Path == worktreePath);
        }
        finally
        {
            TempGitRepoFixture.DeleteDirectory(tempDir.FullName);
        }
    }

    [Fact]
    public void IsReparsePoint_PathDoesNotExist_ReturnsTrue_FailingClosedRatherThanOpen()
    {
        // A TOCTOU race (deleted between being enumerated and being checked) is the realistic case
        // this guards: an unreadable path should be treated as an unfollowable reparse point, not
        // silently treated as a safe plain directory.
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var result = RepoDiscoverer.IsReparsePoint(missingPath, out _);

        Assert.True(result);
    }

    [SkippableFact]
    public void IsReparsePoint_WhenAttributesCannotBeRead_SurfacesTheRealErrorInsteadOfAGenericMessage()
    {
        // Unlike a merely-missing path (which .NET's FileSystemInfo can report via a sentinel
        // attributes value with no exception at all — see the test above, and why it doesn't
        // assert on `error`), a permission-denied directory reliably throws
        // UnauthorizedAccessException cross-platform, which is the case this test pins.
        Skip.If(OperatingSystem.IsWindows(), "There's no equally simple, reliably-restorable way to deny self-access on Windows in a unit test.");

        var tempDir = Directory.CreateTempSubdirectory();
        var lockedDir = Path.Combine(tempDir.FullName, "locked");
        Directory.CreateDirectory(lockedDir);
        try
        {
#pragma warning disable CA1416 // unreachable on Windows: guarded by Skip.If(OperatingSystem.IsWindows()) above
            File.SetUnixFileMode(lockedDir, UnixFileMode.None);
#pragma warning restore CA1416

            var enforced = false;
            try
            {
                _ = new DirectoryInfo(Path.Combine(lockedDir, "child")).Attributes;
            }
            catch (UnauthorizedAccessException)
            {
                enforced = true;
            }

            Skip.IfNot(enforced, "Running with elevated privileges (e.g. root), which bypasses the restriction.");

            var result = RepoDiscoverer.IsReparsePoint(Path.Combine(lockedDir, "child"), out var error);

            Assert.True(result);
            Assert.IsAssignableFrom<UnauthorizedAccessException>(error);
        }
        finally
        {
#pragma warning disable CA1416 // unreachable on Windows: guarded by Skip.If(OperatingSystem.IsWindows()) above
            File.SetUnixFileMode(lockedDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#pragma warning restore CA1416
            tempDir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("node_modules")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".venv")]
    public void Scan_SkipsWellKnownNonRepoDirectoryNames_InsteadOfDescendingIntoThem(string excludedDirectoryName)
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            TempGitRepoFixture.CreateRepo(Path.Combine(tempDir.FullName, excludedDirectoryName), "nested-repo",
                remotes: [("origin", "https://github.com/owner/repo.git")]);

            var result = NewDiscoverer().Scan([tempDir.FullName]);

            Assert.Empty(result.Repos);
            Assert.Contains(result.Skipped, s => s.Path == Path.Combine(tempDir.FullName, excludedDirectoryName));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_DoesNotExcludeAConfiguredRootEvenIfItsNameMatchesAnExcludedDirectoryName()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        var root = Path.Combine(tempDir.FullName, "node_modules");
        try
        {
            TempGitRepoFixture.CreateRepo(root, "repo",
                remotes: [("origin", "https://github.com/owner/repo.git")]);

            var result = NewDiscoverer().Scan([root]);

            Assert.Single(result.Repos);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_ReportsMissingRootsAndContinuesWithOthers()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            TempGitRepoFixture.CreateRepo(tempDir.FullName, "repo",
                remotes: new[] { ("origin", "https://github.com/owner/repo.git") });
            var missingRoot = Path.Combine(tempDir.FullName, "does-not-exist");

            var result = NewDiscoverer().Scan([tempDir.FullName, missingRoot]);

            Assert.Single(result.Repos);
            Assert.Contains(missingRoot, result.MissingRoots);
        }
        finally
        {
            tempDir.Delete(recursive: true);
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
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SkipException("Creating a symlink requires elevated privileges (or Developer Mode) on Windows.", ex);
        }
    }
}
