namespace NugetMcp.Core.Models.Configuration;

public class CodeSimilarityConfiguration
{
    public bool Enabled { get; set; } = false;
    public List<SimplifierConfiguration> Simplifiers { get; set; } = new();
    public List<ComparisonConfiguration> Comparisons { get; set; } = new();
}