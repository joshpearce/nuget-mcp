namespace NugetMcp.Core.Models.Configuration;

public class LevenshteinDistanceSettings
{
    public int MaxDistance { get; set; } = 10;
    public double ThresholdPercent { get; set; } = 0.8;
}