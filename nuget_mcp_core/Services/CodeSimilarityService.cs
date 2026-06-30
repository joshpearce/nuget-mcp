using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NugetMcp.Core.Models;
using NugetMcp.Core.Models.Configuration;
using NugetMcp.Core.Services.CodeSimilarity.Simplifiers;
using NugetMcp.Core.Services.CodeSimilarity.Comparisons;
using System.Collections.Concurrent;

namespace NugetMcp.Core.Services.CodeSimilarity;

public class CodeSimilarityService : ICodeSimilarityService
{
    private readonly ILogger<CodeSimilarityService> _logger;
    private readonly CodeSimilarityConfiguration _config;
    private readonly List<ICodeTextSimplify> _simplifiers;
    private readonly List<ISimilarComparison> _comparisons;
    private readonly ConcurrentDictionary<string, string> _simplificationCache;

    public CodeSimilarityService(
        ILogger<CodeSimilarityService> logger,
        IOptions<CodeSimilarityConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        _simplifiers = new List<ICodeTextSimplify>();
        _comparisons = new List<ISimilarComparison>();
        _simplificationCache = new ConcurrentDictionary<string, string>();

        InitializeSimplifiers();
        InitializeComparisons();
    }

    public async Task<List<PackageUsageInstance>> FilterSimilarUsages(List<PackageUsageInstance> usages)
    {
        if (!_config.Enabled || usages.Count <= 1)
        {
            return usages;
        }

        _logger.LogInformation("Starting similarity filtering for {Count} usages", usages.Count);

        var filteredUsages = new List<PackageUsageInstance>();
        var processedSimplifiedTexts = new HashSet<string>();

        await Task.Run(() =>
        {
            foreach (var usage in usages)
            {
                var simplifiedText = SimplifyCodeText(usage.CodeText);
                
                if (!IsTextSimilarToProcessed(simplifiedText, processedSimplifiedTexts))
                {
                    filteredUsages.Add(usage);
                    processedSimplifiedTexts.Add(simplifiedText);
                }
            }
        });

        var filteredCount = usages.Count - filteredUsages.Count;
        _logger.LogInformation("Filtered {FilteredCount} similar usages, kept {KeptCount}", filteredCount, filteredUsages.Count);

        return filteredUsages;
    }

    public bool AreSimilar(string codeText1, string codeText2)
    {
        if (!_config.Enabled)
            return false;

        var simplified1 = SimplifyCodeText(codeText1);
        var simplified2 = SimplifyCodeText(codeText2);

        return _comparisons.Any(comparison => comparison.AreSimilar(simplified1, simplified2));
    }

    private void InitializeSimplifiers()
    {
        foreach (var simplifierConfig in _config.Simplifiers.Where(s => s.Enabled).OrderBy(s => s.Priority))
        {
            var simplifier = CreateSimplifier(simplifierConfig);
            if (simplifier != null)
            {
                _simplifiers.Add(simplifier);
                _logger.LogDebug("Registered simplifier: {Type} with priority {Priority}", 
                    simplifierConfig.Type, simplifierConfig.Priority);
            }
        }
    }

    private void InitializeComparisons()
    {
        foreach (var comparisonConfig in _config.Comparisons.Where(c => c.Enabled).OrderBy(c => c.Priority))
        {
            var comparison = CreateComparison(comparisonConfig);
            if (comparison != null)
            {
                _comparisons.Add(comparison);
                _logger.LogDebug("Registered comparison: {Type} with priority {Priority}", 
                    comparisonConfig.Type, comparisonConfig.Priority);
            }
        }
    }

    private ICodeTextSimplify? CreateSimplifier(SimplifierConfiguration config)
    {
        return config.Type switch
        {
            "WhitespaceNormalizer" => new WhitespaceNormalizer(config),
            "CommentRemover" => new CommentRemover(config),
            _ => null
        };
    }

    private ISimilarComparison? CreateComparison(ComparisonConfiguration config)
    {
        return config.Type switch
        {
            "LevenshteinDistanceComparison" => new LevenshteinDistanceComparison(config),
            _ => null
        };
    }

    private string SimplifyCodeText(string codeText)
    {
        return _simplificationCache.GetOrAdd(codeText, text =>
        {
            var simplified = text;
            foreach (var simplifier in _simplifiers)
            {
                simplified = simplifier.Simplify(simplified);
            }
            return simplified;
        });
    }

    private bool IsTextSimilarToProcessed(string simplifiedText, HashSet<string> processedTexts)
    {
        return processedTexts.Any(processedText => 
            _comparisons.Any(comparison => comparison.AreSimilar(simplifiedText, processedText)));
    }
}