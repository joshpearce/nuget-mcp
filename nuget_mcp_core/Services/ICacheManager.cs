using NugetMcp.Core.Models;

namespace NugetMcp.Core.Services;

public interface ICacheManager
{
    Task<AnalysisResult?> LoadResultsAsync(string cacheKey);
    Task SaveResultsAsync(string cacheKey, AnalysisResult results);
    Task InvalidateCacheAsync(string cacheKey);
    string GenerateCacheKey(string solutionPath, string packageName, string packageVersion);
    string GenerateCacheKey(string solutionPath, string packageName, string packageVersion, int contextLines);
}