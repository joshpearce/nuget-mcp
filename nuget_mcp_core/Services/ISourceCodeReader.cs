using System.Threading.Tasks;

namespace NugetMcp.Core.Services;

public interface ISourceCodeReader
{
    /// <summary>
    /// Reads lines of code around a specific location in a source file
    /// </summary>
    /// <param name="filePath">Path to the source file</param>
    /// <param name="targetLine">The target line number (1-based)</param>
    /// <param name="contextLines">Number of lines to include above and below target</param>
    /// <returns>Code snippet with context lines</returns>
    Task<CodeSnippet> ReadCodeSnippetAsync(string filePath, int targetLine, int contextLines);
}

public class CodeSnippet
{
    public string Content { get; set; } = string.Empty;
    public int ActualStartLine { get; set; }
    public int ActualEndLine { get; set; }
    public bool WasTruncated { get; set; }
}