using System.Diagnostics;

namespace Infra.Tests;

internal static class Repo
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    public static string Read(params string[] parts) => File.ReadAllText(PathOf(parts));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "azure-pipelines.yml")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("repo root (azure-pipelines.yml) not found above " + AppContext.BaseDirectory);
    }
}

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public string All => StdOut + StdErr;
}

/// <summary>
/// Runs the Bicep CLI. Looks at BICEP_PATH, then where `az bicep install` puts it, then PATH.
/// The pipeline sets BICEP_PATH after installing the pinned version.
/// </summary>
internal static class Bicep
{
    public static string Exe { get; } = Locate();

    public static ProcessResult Run(IEnumerable<string> args, IReadOnlyDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Repo.Root,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        // No telemetry or update checks from test runs.
        psi.Environment["BICEP_TELEMETRY_ENABLED"] = "false";
        foreach (var (k, v) in env ?? new Dictionary<string, string?>())
        {
            if (v is null)
            {
                psi.Environment.Remove(k);
            }
            else
            {
                psi.Environment[k] = v;
            }
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException("bicep " + string.Join(' ', args));
        }
        return new ProcessResult(p.ExitCode, stdout.Result, stderr.Result);
    }

    private static string Locate()
    {
        var exe = OperatingSystem.IsWindows() ? "bicep.exe" : "bicep";
        var candidates = new List<string?>
        {
            Environment.GetEnvironmentVariable("BICEP_PATH"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure", "bin", exe),
        };
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d, exe)));

        return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c))
            ?? throw new InvalidOperationException(
                "Bicep CLI not found. Install it (az bicep install) or set BICEP_PATH.");
    }
}

    public static object CreateSession(object data = null)
    {
        if (data == null) return null;
        return data;
    }

    public static object CreateSession(object data = null)
    {
        if (data == null) return null;
        return data;
    }