using NugetMcp.Core.Models.Configuration;

namespace NugetMcp.Core.Services.CodeSimilarity.Comparisons;

public class LevenshteinDistanceComparison(ComparisonConfiguration config) : ISimilarComparison
{
    private readonly ComparisonConfiguration _config = config;
    private readonly int _maxDistance = config.Settings?.MaxDistance ?? 10;
    private readonly double _thresholdPercent = config.Settings?.ThresholdPercent ?? 0.8;

    public int Priority => _config.Priority;

    public bool AreSimilar(string codeText1, string codeText2)
    {
        if (string.IsNullOrEmpty(codeText1) && string.IsNullOrEmpty(codeText2))
            return true;
        
        if (string.IsNullOrEmpty(codeText1) || string.IsNullOrEmpty(codeText2))
            return false;

        if (codeText1 == codeText2)
            return true;

        var distance = CalculateLevenshteinDistance(codeText1, codeText2);
        
        if (distance <= _maxDistance)
            return true;

        var maxLength = Math.Max(codeText1.Length, codeText2.Length);
        var similarity = 1.0 - (double)distance / maxLength;
        
        return similarity >= _thresholdPercent;
    }

    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (source.Length == 0) return target.Length;
        if (target.Length == 0) return source.Length;

        var matrix = new int[source.Length + 1, target.Length + 1];

        for (int i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[source.Length, target.Length];
    }
}