namespace NugetMcp.Core.Models;

public class AnalysisResult
{
    public required string SolutionPath { get; set; }
    public required string PackageName { get; set; }
    public required string PackageVersion { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public TimeSpan AnalysisDuration { get; set; }
    public List<PackageUsageInstance> Usages { get; set; } = new();
    public List<string> AnalyzedProjects { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int TotalUsageCount => Usages.Count;
}