using NugetMcp.Core.Models;

namespace NugetMcp.Core.Services.CodeSimilarity;

public interface ICodeSimilarityService
{
    Task<List<PackageUsageInstance>> FilterSimilarUsages(List<PackageUsageInstance> usages);
    bool AreSimilar(string codeText1, string codeText2);
}