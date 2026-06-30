using Microsoft.CodeAnalysis;

namespace NugetMcp.Core.Services;

public interface IPackageAssemblyResolver
{
    Task<HashSet<string>> ResolvePackageAssembliesAsync(string packageName, string packageVersion, Project project);
}