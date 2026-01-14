using System.Diagnostics;
using System.Text;

namespace Blackpaw.Tests.EndToEnd;

/// <summary>
/// Helper class to build and launch test applications for E2E testing.
/// Handles building the test app projects and starting them as processes.
/// </summary>
public sealed class TestAppLauncher : IAsyncDisposable
{
    private Process? _process;
    private readonly string _projectPath;
    private readonly string _framework;
    private readonly StringBuilder _outputBuilder = new();
    private readonly StringBuilder _errorBuilder = new();

    /// <summary>
    /// The port the HTTP server is listening on (for .NET Core app).
    /// </summary>
    public int? Port { get; private set; }

    /// <summary>
    /// The process ID of the running test app.
    /// </summary>
    public int ProcessId => _process?.Id ?? 0;

    /// <summary>
    /// The process name (without extension).
    /// </summary>
    public string ProcessName { get; }

    /// <summary>
    /// Whether the process is still running.
    /// </summary>
    public bool IsRunning => _process != null && !_process.HasExited;

    /// <summary>
    /// All standard output collected from the process.
    /// </summary>
    public string StandardOutput => _outputBuilder.ToString();

    /// <summary>
    /// All standard error collected from the process.
    /// </summary>
    public string StandardError => _errorBuilder.ToString();

    private TestAppLauncher(string projectPath, string framework, string processName)
    {
        _projectPath = projectPath;
        _framework = framework;
        ProcessName = processName;
    }

    /// <summary>
    /// Starts the .NET Core test app which hosts its own HTTP server.
    /// </summary>
    /// <param name="durationSeconds">How long the app should run before self-terminating.</param>
    /// <returns>A launcher with the running process.</returns>
    public static async Task<TestAppLauncher> StartNetCoreAppAsync(int durationSeconds)
    {
        var launcher = new TestAppLauncher(
            GetProjectPath("Blackpaw.TestApp.NetCore"),
            "net10.0",
            "Blackpaw.TestApp.NetCore");
        await launcher.BuildAndStartAsync(durationSeconds.ToString());
        return launcher;
    }

    /// <summary>
    /// Starts the .NET Framework test app which connects to the provided server URL.
    /// </summary>
    /// <param name="serverUrl">The URL of the HTTP server to connect to.</param>
    /// <param name="durationSeconds">How long the app should run before self-terminating.</param>
    /// <returns>A launcher with the running process.</returns>
    public static async Task<TestAppLauncher> StartNetFrameworkAppAsync(string serverUrl, int durationSeconds)
    {
        var launcher = new TestAppLauncher(
            GetProjectPath("Blackpaw.TestApp.NetFramework"),
            "net48",
            "Blackpaw.TestApp.NetFramework");
        await launcher.BuildAndStartAsync($"\"{serverUrl}\" {durationSeconds}");
        return launcher;
    }

    private static string GetProjectPath(string projectName)
    {
        // Find the repository root by looking for the src folder or tests folder
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (directory != null)
        {
            // Check for repository structure indicators
            var srcDir = Path.Combine(directory.FullName, "src");
            var testsDir = Path.Combine(directory.FullName, "tests");

            if (Directory.Exists(srcDir) && Directory.Exists(testsDir))
            {
                var projectPath = Path.Combine(testsDir, "TestApps", projectName, $"{projectName}.csproj");
                if (File.Exists(projectPath))
                {
                    return projectPath;
                }
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find repository root directory. " +
            $"Started from: {AppDomain.CurrentDomain.BaseDirectory}. " +
            $"Looking for: tests/TestApps/{projectName}/{projectName}.csproj");
    }

    private async Task BuildAndStartAsync(string args)
    {
        // Build the project
        _outputBuilder.Clear();
        _errorBuilder.Clear();
        var buildExitCode = await RunDotNetAsync($"build \"{_projectPath}\" -c Release -f {_framework}");
        if (buildExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Build failed with exit code {buildExitCode}.\n" +
                $"Project: {_projectPath}\n" +
                $"Output: {_outputBuilder}\n" +
                $"Error: {_errorBuilder}");
        }

        // Find the executable
        var exePath = FindExecutable();
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"Executable not found at: {exePath}");
        }

        // Clear output builders for process output
        _outputBuilder.Clear();
        _errorBuilder.Clear();

        // Start the process
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            },
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _outputBuilder.AppendLine(e.Data);
            }
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _errorBuilder.AppendLine(e.Data);
            }
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // Wait for the first output line which should contain PORT: or STARTED:
        var timeout = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < timeout)
        {
            var output = _outputBuilder.ToString();
            if (output.Contains("PORT:"))
            {
                var portLine = output.Split('\n').FirstOrDefault(l => l.StartsWith("PORT:"));
                if (portLine != null)
                {
                    Port = int.Parse(portLine.Substring(5).Trim());
                    break;
                }
            }
            else if (output.Contains("STARTED:"))
            {
                // Framework app doesn't have a port
                break;
            }

            if (_process.HasExited)
            {
                throw new InvalidOperationException($"Process exited prematurely. Output: {output}, Error: {_errorBuilder}");
            }

            await Task.Delay(100);
        }

        if (DateTime.UtcNow >= timeout)
        {
            throw new TimeoutException($"Timed out waiting for app to start. Output: {_outputBuilder}");
        }

        // Give the app a moment to fully initialize
        await Task.Delay(500);
    }

    private async Task<int> RunDotNetAsync(string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) _outputBuilder.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) _errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private string FindExecutable()
    {
        var projectDir = Path.GetDirectoryName(_projectPath)!;
        var binDir = Path.Combine(projectDir, "bin", "Release", _framework);

        // For .NET Core, the executable is a .dll run with dotnet
        // For .NET Framework, it's a .exe
        if (_framework.StartsWith("net4"))
        {
            // .NET Framework - look for .exe
            return Path.Combine(binDir, $"{ProcessName}.exe");
        }
        else
        {
            // .NET Core - look for .exe (self-contained) or .dll
            var exePath = Path.Combine(binDir, $"{ProcessName}.exe");
            if (File.Exists(exePath))
            {
                return exePath;
            }
            return Path.Combine(binDir, $"{ProcessName}.dll");
        }
    }

    /// <summary>
    /// Waits for the process to complete or timeout.
    /// </summary>
    public async Task WaitForExitAsync(TimeSpan? timeout = null)
    {
        if (_process == null) return;

        using var cts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();

        try
        {
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout - process still running
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch
            {
                // Best effort cleanup
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }
    }
}
