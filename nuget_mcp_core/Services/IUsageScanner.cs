using Microsoft.CodeAnalysis;
using NugetMcp.Core.Models;

namespace NugetMcp.Core.Services;

public interface IUsageScanner
{
    Task<List<PackageUsageInstance>> ScanProjectAsync(Project project, HashSet<string> targetAssemblies, int contextLines = 0);
    Task<List<PackageUsageInstance>> ScanProjectForSymbolsAsync(Project project, HashSet<string> targetSymbols, int contextLines = 0);
}