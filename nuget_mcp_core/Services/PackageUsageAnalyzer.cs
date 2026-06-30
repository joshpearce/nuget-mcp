using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NugetMcp.Core.Models.Configuration;
using NugetMcp.Core.Models;
using NugetMcp.Core.Services.CodeSimilarity;
using System.Diagnostics;

namespace NugetMcp.Core.Services;

public class PackageUsageAnalyzer : IPackageUsageAnalyzer
{
    private readonly ISolutionLoader _solutionLoader;
    private readonly IPackageAssemblyResolver _assemblyResolver;
    private readonly IUsageScanner _usageScanner;
    private readonly ICacheManager _cacheManager;
    private readonly IParallelExecutor _parallelExecutor;
    private readonly ICodeSimilarityService? _similarityService;
    private readonly UsageTypeFilterConfiguration _usageTypeConfig;
    private readonly ILogger<PackageUsageAnalyzer> _logger;

    public PackageUsageAnalyzer(
        ISolutionLoader solutionLoader,
        IPackageAssemblyResolver assemblyResolver,
        IUsageScanner usageScanner,
        ICacheManager cacheManager,
        IParallelExecutor parallelExecutor,
        ICodeSimilarityService? similarityService,
        IOptions<UsageTypeFilterConfiguration> usageTypeConfig,
        ILogger<PackageUsageAnalyzer> logger)
    {
        _solutionLoader = solutionLoader;
        _assemblyResolver = assemblyResolver;
        _usageScanner = usageScanner;
        _cacheManager = cacheManager;
        _parallelExecutor = parallelExecutor;
        _similarityService = similarityService;
        _usageTypeConfig = usageTypeConfig.Value;
        _logger = logger;
    }

    public async Task<AnalysisResult> AnalyzeAsync(string solutionPath, string packageName, string packageVersion, bool forceRefresh = false, int contextLines = 0, IEnumerable<UsageType>? usageTypeFilters = null)
    {
        _logger.LogInformation("Starting analysis of package {PackageName}@{PackageVersion} in solution {SolutionPath} (forceRefresh: {ForceRefresh})",
            packageName, packageVersion, solutionPath, forceRefresh);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cacheKey = _cacheManager.GenerateCacheKey(solutionPath, packageName, packageVersion, contextLines);

            if (!forceRefresh)
            {
                var cachedResult = await _cacheManager.LoadResultsAsync(cacheKey);
                if (cachedResult != null)
                {
                    _logger.LogInformation("Returning cached analysis results for {PackageName}@{PackageVersion} with {UsageCount} usages",
                        packageName, packageVersion, cachedResult.TotalUsageCount);
                    return cachedResult;
                }
            }

            var result = await PerformAnalysisAsync(solutionPath, packageName, packageVersion, contextLines, usageTypeFilters);
            result.AnalysisDuration = stopwatch.Elapsed;

            await _cacheManager.SaveResultsAsync(cacheKey, result);

            _logger.LogInformation("Completed analysis of {PackageName}@{PackageVersion} in {Duration:F2}s with {UsageCount} usages across {ProjectCount} projects",
                packageName, packageVersion, stopwatch.Elapsed.TotalSeconds, result.TotalUsageCount, result.AnalyzedProjects.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze package {PackageName}@{PackageVersion} in solution {SolutionPath}",
                packageName, packageVersion, solutionPath);
            
            return new AnalysisResult
            {
                SolutionPath = solutionPath,
                PackageName = packageName,
                PackageVersion = packageVersion,
                AnalyzedAt = DateTime.UtcNow,
                AnalysisDuration = stopwatch.Elapsed,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    private async Task<AnalysisResult> PerformAnalysisAsync(string solutionPath, string packageName, string packageVersion, int contextLines, IEnumerable<UsageType>? usageTypeFilters = null)
    {
        var result = new AnalysisResult
        {
            SolutionPath = solutionPath,
            PackageName = packageName,
            PackageVersion = packageVersion,
            AnalyzedAt = DateTime.UtcNow
        };

        _logger.LogDebug("Loading solution from {SolutionPath}", solutionPath);
        var solution = await _solutionLoader.LoadSolutionAsync(solutionPath);

        var projects = solution.Projects.ToList();
        _logger.LogDebug("Found {ProjectCount} projects to analyze", projects.Count);

        var allUsages = new List<PackageUsageInstance>();

        var projectResults = await _parallelExecutor.ExecuteParallelAsync(projects, async project =>
        {
            try
            {
                _logger.LogDebug("Analyzing project {ProjectName}", project.Name);
                
                var assemblies = await _assemblyResolver.ResolvePackageAssembliesAsync(packageName, packageVersion, project);
                if (assemblies.Count == 0)
                {
                    _logger.LogDebug("No assemblies found for package {PackageName}@{PackageVersion} in project {ProjectName}",
                        packageName, packageVersion, project.Name);
                    return new ProjectAnalysisResult(project.Name, new List<PackageUsageInstance>(), null);
                }

                var usages = await _usageScanner.ScanProjectAsync(project, assemblies, contextLines);
                _logger.LogDebug("Found {UsageCount} usages in project {ProjectName}", usages.Count, project.Name);
                
                return new ProjectAnalysisResult(project.Name, usages, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze project {ProjectName}", project.Name);
                return new ProjectAnalysisResult(project.Name, new List<PackageUsageInstance>(), ex.Message);
            }
        });

        foreach (var projectResult in projectResults)
        {
            result.AnalyzedProjects.Add(projectResult.ProjectName);
            allUsages.AddRange(projectResult.Usages);
            
            if (projectResult.Error != null)
            {
                result.Errors.Add($"Project {projectResult.ProjectName}: {projectResult.Error}");
            }
        }

        result.Usages = allUsages;

        // Apply similarity filtering if enabled
        if (_similarityService != null)
        {
            _logger.LogDebug("Applying similarity filtering to {UsageCount} usages", allUsages.Count);
            var filteredUsages = await _similarityService.FilterSimilarUsages(allUsages);
            result.Usages = filteredUsages.ToList();
            _logger.LogDebug("Similarity filtering reduced usages from {OriginalCount} to {FilteredCount}",
                allUsages.Count, result.Usages.Count);
        }

        // Apply usage type filtering
        result.Usages = FilterByUsageTypes(result.Usages, usageTypeFilters).ToList();

        _logger.LogDebug("Analysis completed with {TotalUsages} total usages and {ErrorCount} errors",
            result.TotalUsageCount, result.Errors.Count);

        return result;
    }

    private IEnumerable<PackageUsageInstance> FilterByUsageTypes(IEnumerable<PackageUsageInstance> usages, IEnumerable<UsageType>? usageTypeFilters)
    {
        // If no specific filters provided, use configured filters from appsettings
        var filters = usageTypeFilters?.ToList() ?? ParseConfiguredUsageTypes();
        
        // If no filters configured or provided, return all usages
        if (filters == null || !filters.Any())
        {
            return usages;
        }

        var beforeCount = usages.Count();
        var filtered = usages.Where(u => filters.Contains(u.Type));
        var afterCount = filtered.Count();

        _logger.LogDebug("Usage type filtering reduced usages from {OriginalCount} to {FilteredCount} using filters: {Filters}",
            beforeCount, afterCount, string.Join(", ", filters));

        return filtered;
    }

    private List<UsageType>? ParseConfiguredUsageTypes()
    {
        if (_usageTypeConfig.IncludedUsageTypes == null || !_usageTypeConfig.IncludedUsageTypes.Any())
        {
            return null;
        }

        var parsedTypes = new List<UsageType>();
        foreach (var typeString in _usageTypeConfig.IncludedUsageTypes)
        {
            if (Enum.TryParse<UsageType>(typeString, true, out var usageType))
            {
                parsedTypes.Add(usageType);
            }
            else
            {
                _logger.LogWarning("Invalid usage type '{UsageType}' in configuration. Valid types: {ValidTypes}",
                    typeString, string.Join(", ", Enum.GetNames<UsageType>()));
            }
        }

        return parsedTypes.Any() ? parsedTypes : null;
    }

    public async Task<AnalysisResult> AnalyzeSymbolAsync(string solutionPath, string targetSymbol, bool forceRefresh = false, int contextLines = 0, IEnumerable<UsageType>? usageTypeFilters = null)
    {
        _logger.LogInformation("Starting symbol analysis of {TargetSymbol} in solution {SolutionPath} (forceRefresh: {ForceRefresh})",
            targetSymbol, solutionPath, forceRefresh);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var cacheKey = _cacheManager.GenerateCacheKey(solutionPath, targetSymbol, "symbol", contextLines);

            if (!forceRefresh)
            {
                var cachedResult = await _cacheManager.LoadResultsAsync(cacheKey);
                if (cachedResult != null)
                {
                    _logger.LogInformation("Returning cached symbol analysis results for {TargetSymbol} with {UsageCount} usages",
                        targetSymbol, cachedResult.TotalUsageCount);
                    return cachedResult;
                }
            }

            var solution = await _solutionLoader.LoadSolutionAsync(solutionPath);
            var targetSymbols = new HashSet<string> { targetSymbol };

            var projectResults = await _parallelExecutor.ExecuteParallelAsync(solution.Projects, async project =>
            {
                try
                {
                    var usages = await _usageScanner.ScanProjectForSymbolsAsync(project, targetSymbols, contextLines);
                    return new ProjectAnalysisResult(project.Name, usages, null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to analyze project {ProjectName}", project.Name);
                    return new ProjectAnalysisResult(project.Name, [], ex.Message);
                }
            });

            var result = new AnalysisResult
            {
                SolutionPath = solutionPath,
                PackageName = $"symbol:{targetSymbol}",
                PackageVersion = "N/A",
                AnalyzedAt = DateTime.UtcNow,
                AnalysisDuration = stopwatch.Elapsed,
                Usages = [],
                Errors = []
            };

            var allUsages = new List<PackageUsageInstance>();
            
            foreach (var projectResult in projectResults)
            {
                allUsages.AddRange(projectResult.Usages);
                
                if (projectResult.Error != null)
                {
                    result.Errors.Add($"Project {projectResult.ProjectName}: {projectResult.Error}");
                }
            }

            result.Usages = allUsages;

            // Apply similarity filtering if enabled
            if (_similarityService != null)
            {
                _logger.LogDebug("Applying similarity filtering to {UsageCount} symbol usages", allUsages.Count);
                var filteredUsages = await _similarityService.FilterSimilarUsages(allUsages);
                result.Usages = filteredUsages.ToList();
                _logger.LogDebug("Similarity filtering reduced symbol usages from {OriginalCount} to {FilteredCount}",
                    allUsages.Count, result.Usages.Count);
            }

            // Apply usage type filtering
            result.Usages = FilterByUsageTypes(result.Usages, usageTypeFilters).ToList();

            await _cacheManager.SaveResultsAsync(cacheKey, result);

            _logger.LogInformation("Symbol analysis completed for {TargetSymbol} with {TotalUsages} total usages and {ErrorCount} errors in {ElapsedMs}ms",
                targetSymbol, result.TotalUsageCount, result.Errors.Count, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze symbol {TargetSymbol} in solution {SolutionPath}", targetSymbol, solutionPath);
            throw;
        }
    }

    private record ProjectAnalysisResult(string ProjectName, List<PackageUsageInstance> Usages, string? Error);
}