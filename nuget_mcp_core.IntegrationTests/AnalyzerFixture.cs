using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NugetMcp.Core.Extensions;
using NugetMcp.Core.Services;

namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Builds the same DI graph as the production frontends (via <see cref="ServiceCollectionExtensions.AddNugetMcpCore"/>)
/// once per test collection and exposes the resolved <see cref="IPackageUsageAnalyzer"/> for integration tests to share.
/// </summary>
public class AnalyzerFixture
{
    public IPackageUsageAnalyzer Analyzer { get; }

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

        var provider = services.BuildServiceProvider();
        Analyzer = provider.GetRequiredService<IPackageUsageAnalyzer>();
    }
}

/// <summary>
/// Pairs all integration tests under a single xUnit collection so they share one <see cref="AnalyzerFixture"/>
/// and never run concurrently against the shared NuGet/analysis caches.
/// </summary>
[CollectionDefinition("AnalyzerCollection")]
public class AnalyzerCollection : ICollectionFixture<AnalyzerFixture>
{
}
