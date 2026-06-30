using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NugetMcp.Core.Extensions;
using NugetMcp.Core.Services;

namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Builds the same DI graph as the production frontends (via <see cref="ServiceCollectionExtensions.AddNugetMcpCore"/>)
/// once per test collection and exposes the resolved <see cref="IPackageUsageAnalyzer"/> for integration tests to share.
/// </summary>
public class AnalyzerFixture : IAsyncLifetime
{
    public IPackageUsageAnalyzer Analyzer { get; }

    private readonly ServiceProvider _provider;

    static AnalyzerFixture()
    {
        // Disables MSBuild's node-reuse IPC. Without this, the in-process MSBuildWorkspace used by
        // RoslynSolutionLoader (for symbol analysis) and the `dotnet restore` child process shelled
        // out by NuGetPackageAssemblyResolver (for package analysis) can negotiate over the same
        // node-reuse pipe and deadlock when both touch the same solution within this test process
        // -- reproduced reliably running RestSharpFixtureTests' symbol test followed by its package
        // test, hanging until the per-test timeout. Setting this before any MSBuild activity starts
        // (and before the env var is read by ProcessStartInfo for the restore child process) avoids
        // the deadlock; the production frontends never run two analyses in one process, so they
        // don't need this.
        Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
    }

    public AnalyzerFixture()
    {
        var configuration = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        // AddNugetMcpCore expects a host that already registers logging (all three production
        // frontends build on Host.CreateApplicationBuilder, which does this automatically).
        // We build a bare ServiceCollection here, so logging must be added explicitly for
        // ILogger<T> dependencies to resolve.
        services.AddLogging();
        services.AddNugetMcpCore(configuration);

        _provider = services.BuildServiceProvider();
        Analyzer = _provider.GetRequiredService<IPackageUsageAnalyzer>();
    }

    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(5);

    public async Task InitializeAsync()
    {
        // PackageUsageAnalyzer.AnalyzeAsync scans every project in a solution in parallel, and each
        // project independently calls NuGetPackageAssemblyResolver.EnsurePackagesRestoredAsync,
        // which shells out to `dotnet restore` for the whole solution -- with no de-duplication or
        // locking across those concurrent calls. On a solution that has never been restored before
        // (e.g. a freshly checked-out submodule, before any obj/*.nuget.* files exist), that becomes
        // N simultaneous `dotnet restore` invocations against the same solution, which race on the
        // same obj/ output files and fail (reproduced reliably: 10 concurrent restores against
        // eShopOnWeb after `git submodule deinit && update --init`, all exiting 1, leaving every
        // project's package-assembly resolution empty -- 0 usages found instead of the expected
        // ~25). A single sequential restore per fixture solution here, before any test runs, makes
        // the analyzer's later per-project restores fast up-to-date no-ops instead of a race. This
        // mitigates a real concurrency gap in NuGetPackageAssemblyResolver; fixing it there is a
        // suggested follow-up (see nuget_mcp_core/CLAUDE.md), out of scope for this test-only fix.
        foreach (var solutionPath in new[] { FixtureRepoPaths.RestSharpSolution, FixtureRepoPaths.EShopOnWebSolution })
        {
            await RestoreAsync(solutionPath);
        }
    }

    private static async Task RestoreAsync(string solutionPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore \"{solutionPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start 'dotnet restore' for {solutionPath}.");

        // Both streams must be drained concurrently with the wait, not just stderr: if `dotnet
        // restore` writes enough stdout to fill the OS pipe buffer (cold cache + restore warnings
        // can produce far more output than a warm-cache run), the child blocks writing to a full,
        // unread pipe and never exits -- a classic Process redirect-without-drain deadlock, and
        // exactly the cold-cache scenario this pre-warm exists for.
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(RestoreTimeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException(
                $"Pre-warm 'dotnet restore {solutionPath}' did not complete within {RestoreTimeout}.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Pre-warm 'dotnet restore {solutionPath}' failed with exit code {process.ExitCode}: " +
                $"{await stdErrTask}{await stdOutTask}");
        }
    }

    public Task DisposeAsync()
    {
        _provider.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Pairs all integration tests under a single xUnit collection so they share one <see cref="AnalyzerFixture"/>
/// and never run concurrently against the shared NuGet/analysis caches. <c>DisableParallelization</c> is set
/// explicitly even though a single collection already serializes execution by default, so the no-concurrent-
/// access intent doesn't depend on the reader knowing xUnit's default.
/// </summary>
[CollectionDefinition("AnalyzerCollection", DisableParallelization = true)]
public class AnalyzerCollection : ICollectionFixture<AnalyzerFixture>
{
}
