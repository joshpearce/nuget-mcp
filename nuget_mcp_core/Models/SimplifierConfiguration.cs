namespace NugetMcp.Core.Models.Configuration;

public class SimplifierConfiguration
{
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 1;
}