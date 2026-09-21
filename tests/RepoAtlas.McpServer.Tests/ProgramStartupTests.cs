using System.Diagnostics;

namespace RepoAtlas.McpServer.Tests;

public class ProgramStartupTests
{
    [Fact]
    public async Task Startup_WithMalformedDataFile_FailsFastWithClearMessageAndNonZeroExitCode()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var dataPath = Path.Combine(tempDir.FullName, "data.yaml");
            File.WriteAllText(dataPath, "repos:\n\tinvalid-tab-indentation: true");

            var serverDllPath = typeof(RepoCatalog).Assembly.Location;

            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(serverDllPath);
            startInfo.Environment["REPOATLAS_DATA_PATH"] = dataPath;

            using var process = Process.Start(startInfo)!;
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var exited = process.WaitForExit(TimeSpan.FromSeconds(30));
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
            }

            Assert.True(exited, "The server process did not exit within the timeout.");
            Assert.NotEqual(0, process.ExitCode);

            var stderr = await stderrTask;
            await stdoutTask;
            Assert.Contains("RepoAtlas failed to start", stderr);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Startup_WithNoDataFileAndImmediateStdinClose_ExitsCleanlyWithoutFailureMessage()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var dataPath = Path.Combine(tempDir.FullName, "data.yaml");
            var serverDllPath = typeof(RepoCatalog).Assembly.Location;

            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(serverDllPath);
            startInfo.Environment["REPOATLAS_DATA_PATH"] = dataPath;

            using var process = Process.Start(startInfo)!;
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var exited = process.WaitForExit(TimeSpan.FromSeconds(30));
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
            }

            Assert.True(exited, "The server process did not exit within the timeout.");
            Assert.Equal(0, process.ExitCode);

            var stderr = await stderrTask;
            await stdoutTask;
            Assert.DoesNotContain("RepoAtlas failed to start", stderr);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
