using System.Diagnostics;
using Xunit;

namespace Blackpaw.Tests.TestHelpers;

/// <summary>
/// Fact attribute that skips the test when Docker is not available.
/// Used for integration tests that require Docker containers.
/// </summary>
public class DockerRequiredFact : FactAttribute
{
    public DockerRequiredFact()
    {
        if (!IsDockerAvailable())
        {
            Skip = "Docker is not available on this machine";
        }
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
