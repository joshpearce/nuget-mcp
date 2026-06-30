using Microsoft.CodeAnalysis;

namespace NugetMcp.Core.Services;

public interface ISolutionLoader
{
    Task<Solution> LoadSolutionAsync(string solutionPath);
}