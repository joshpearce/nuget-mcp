using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NugetMcp.Core.Extensions;
using NugetMcp.Core.Services;

namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Builds the same DI graph as the production frontends (via <see cref="ServiceCollectionExtensions.AddNugetMcpCore"/>)
/// once per test collection and exposes the resolved <see cref="IPackageUsageAnalyzer"/> for integration tests to share.
/// </summary>
public class AnalyzerFixture : IDisposable
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

    public void Dispose()
    {
        _provider.Dispose();
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
