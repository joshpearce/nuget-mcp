namespace NugetMcp.Core.Services.CodeSimilarity;

public interface ISimilarComparison
{
    bool AreSimilar(string codeText1, string codeText2);
    int Priority { get; }
}