namespace NugetMcp.Core.Models.Configuration;

public class ComparisonConfiguration
{
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
    public LevenshteinDistanceSettings? Settings { get; set; }
}