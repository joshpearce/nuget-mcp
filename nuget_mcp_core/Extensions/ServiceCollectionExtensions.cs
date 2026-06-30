using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NugetMcp.Core.Models.Configuration;
using NugetMcp.Core.Services;
using NugetMcp.Core.Services.CodeSimilarity;

namespace NugetMcp.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNugetMcpCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICacheManager, FileCacheManager>();
        services.AddSingleton<ISolutionLoader, RoslynSolutionLoader>();
        services.AddSingleton<IPackageAssemblyResolver, NuGetPackageAssemblyResolver>();
        services.AddSingleton<ISourceCodeReader, SourceCodeReader>();
        services.AddSingleton<IUsageScanner, RoslynUsageScanner>();
        services.AddSingleton<IParallelExecutor, TaskParallelExecutor>();
        services.AddSingleton<IPackageUsageAnalyzer, PackageUsageAnalyzer>();

        // Configure CodeSimilarity
        services.Configure<CodeSimilarityConfiguration>(
            configuration.GetSection("CodeSimilarity"));
        services.AddSingleton<ICodeSimilarityService, CodeSimilarityService>();

        // Configure UsageTypeFilter
        services.Configure<UsageTypeFilterConfiguration>(
            configuration.GetSection("UsageTypeFilter"));

        return services;
    }
}
