namespace NugetMcp.Core.Services.CodeSimilarity;

public interface ICodeTextSimplify
{
    string Simplify(string codeText);
    int Priority { get; }
}