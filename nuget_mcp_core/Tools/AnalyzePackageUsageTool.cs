using ModelContextProtocol.Server;
using NugetMcp.Core.Services;
using NugetMcp.Core.Utilities;
using System.ComponentModel;
using System.Text.Json;

namespace NugetMcp.Core.Tools;

[McpServerToolType]
public static class AnalyzePackageUsageTool
{
    private static IPackageUsageAnalyzer? _analyzer;

    public static void Initialize(IPackageUsageAnalyzer analyzer)
    {
        _analyzer = analyzer;
    }

    [McpServerTool, Description("Analyzes a .NET solution to find all usage of a specified NuGet package. Returns detailed information about where and how the package is used in the codebase.")]
    public static async Task<string> AnalyzePackageUsage(
        [Description("Path to the .NET solution file (.sln)")]
        string solutionPath,
        [Description("Name of the NuGet package to analyze")]
        string packageName,
        [Description("Version of the NuGet package to analyze")]
        string packageVersion,
        [Description("Number of context lines to include above and below each usage (default: 0)")]
        int contextLines = 0)
    {
        if (_analyzer == null)
        {
            throw new InvalidOperationException("Package usage analyzer has not been initialized");
        }

        try
        {
            var result = await _analyzer.AnalyzeAsync(solutionPath, packageName, packageVersion, false, contextLines);
            
            var response = new
            {
                summary = new
                {
                    solutionPath = result.SolutionPath,
                    packageName = result.PackageName,
                    packageVersion = result.PackageVersion,
                    analyzedAt = result.AnalyzedAt,
                    analysisDuration = result.AnalysisDuration.ToString(@"hh\:mm\:ss\.fff"),
                    totalUsageCount = result.TotalUsageCount,
                    analyzedProjectCount = result.AnalyzedProjects.Count,
                    errorCount = result.Errors.Count
                },
                analyzedProjects = result.AnalyzedProjects,
                usages = result.Usages.Select(u => new
                {
                    projectName = u.ProjectName,
                    filePath = u.FilePath,
                    startLine = u.StartLine,
                    endLine = u.EndLine,
                    codeText = u.CodeText,
                    usageType = u.Type,
                    symbolName = u.SymbolName,
                    @namespace = u.Namespace,
                    assemblyName = u.AssemblyName
                }).ToArray(),
                errors = result.Errors
            };

            return JsonSerializer.Serialize(response, JsonSerializationHelper.GetDefaultOptions());
        }
        catch (Exception ex)
        {
            var errorResponse = new
            {
                error = ex.Message,
                solutionPath,
                packageName,
                packageVersion,
                timestamp = DateTime.UtcNow
            };

            return JsonSerializer.Serialize(errorResponse, JsonSerializationHelper.GetDefaultOptions());
        }
    }
}