namespace NugetMcp.Core.Models;

public class PackageUsageInstance
{
    public required string ProjectName { get; set; }
    public required string FilePath { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public required string CodeText { get; set; }
    public UsageType Type { get; set; }
    public required string SymbolName { get; set; }
    public string? Namespace { get; set; }
    public string? AssemblyName { get; set; }
    
    // New properties for enhanced context
    public int ContextStartLine { get; set; }
    public int ContextEndLine { get; set; }
    public bool HasContext { get; set; }
}