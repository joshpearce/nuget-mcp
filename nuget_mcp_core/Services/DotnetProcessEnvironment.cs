using System.Diagnostics;

namespace NugetMcp.Core.Services;

/// <summary>
/// Helpers for spawning <c>dotnet</c> child processes from a host that has already registered
/// MSBuild via <c>MSBuildLocator.RegisterDefaults()</c> (which <see cref="RoslynSolutionLoader"/>
/// does the first time a solution is loaded).
/// </summary>
public static class DotnetProcessEnvironment
{
    // MSBuildLocator.RegisterDefaults() sets these process-wide environment variables to point at
    // the SDK instance it selected for the *running runtime* (e.g. the SDK matching net8.0 when the
    // host runs on .NET 8). A `dotnet restore`/build child process spawned afterwards inherits them,
    // but the `dotnet` muxer independently selects an SDK by version/global.json -- typically the
    // newest installed. When those disagree (e.g. MSBuildLocator picked 8.0.422 but the muxer picks
    // 10.0.301 because it's newest and there's no global.json), the child loads one SDK's muxer while
    // these variables force a different SDK's MSBuild assemblies/targets, and `dotnet restore` dies
    // immediately with exit code 1 and *no* stdout or stderr. Removing them lets the child resolve a
    // single, self-consistent SDK on its own -- exactly as it would from a plain shell.
    private static readonly string[] MSBuildLocatorVariables =
    {
        "MSBUILD_EXE_PATH",
        "MSBuildExtensionsPath",
        "MSBuildSDKsPath",
    };

    /// <summary>
    /// Removes the environment variables that <c>MSBuildLocator.RegisterDefaults()</c> injects into
    /// this process, so a spawned <c>dotnet</c> child resolves its own SDK instead of inheriting the
    /// host's MSBuild-locator selection. Call this on every <c>dotnet</c> <see cref="ProcessStartInfo"/>
    /// that runs MSBuild (restore/build) with <c>UseShellExecute = false</c>.
    /// </summary>
    public static void StripMSBuildLocatorVariables(ProcessStartInfo startInfo)
    {
        foreach (var name in MSBuildLocatorVariables)
        {
            startInfo.Environment.Remove(name);
        }
    }
}
