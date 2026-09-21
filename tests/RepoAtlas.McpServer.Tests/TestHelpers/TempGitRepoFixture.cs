using LibGit2Sharp;

namespace RepoAtlas.McpServer.Tests.TestHelpers;

public static class TempGitRepoFixture
{
    public static string CreateRepo(
        string rootDirectory,
        string relativePath,
        IEnumerable<(string Name, string Url)>? remotes = null,
        bool withCommit = false,
        string? readmeContent = null)
    {
        var path = Path.Combine(rootDirectory, relativePath);
        Directory.CreateDirectory(path);
        Repository.Init(path);

        using var repo = new Repository(path);

        if (remotes is not null)
        {
            foreach (var (name, url) in remotes)
            {
                repo.Network.Remotes.Add(name, url);
            }
        }

        if (readmeContent is not null)
        {
            File.WriteAllText(Path.Combine(path, "README.md"), readmeContent);
        }

        if (withCommit)
        {
            Commands.Stage(repo, "*");
            var signature = new Signature("Test", "test@example.com", DateTimeOffset.Now);
            repo.Commit("Initial commit", signature, signature);
        }

        return path;
    }

    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
