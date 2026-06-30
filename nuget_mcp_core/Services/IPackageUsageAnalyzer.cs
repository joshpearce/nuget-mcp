using NugetMcp.Core.Models;

namespace NugetMcp.Core.Services;

public interface IPackageUsageAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(string solutionPath, string packageName, string packageVersion, bool forceRefresh = false, int contextLines = 0, IEnumerable<UsageType>? usageTypeFilters = null);
    /// <summary>
    /// Analyzes a .NET solution to find usage of a symbol (namespace, type, or member).
    /// </summary>
    /// <param name="solutionPath">Path to the .NET solution file (.sln)</param>
    /// <param name="targetSymbol">Symbol to analyze (e.g., 'System.IO', 'System.Diagnostics.Stopwatch', 'Console.WriteLine')</param>
    /// <param name="forceRefresh">Force refresh of cached results</param>
    /// <param name="contextLines">Number of lines to include above and below each usage</param>
    /// <param name="usageTypeFilters">Optional filters to include only specific usage types</param>
    /// <returns>Analysis result with usage details</returns>
    Task<AnalysisResult> AnalyzeSymbolAsync(string solutionPath, string targetSymbol, bool forceRefresh = false, int contextLines = 0, IEnumerable<UsageType>? usageTypeFilters = null);
}