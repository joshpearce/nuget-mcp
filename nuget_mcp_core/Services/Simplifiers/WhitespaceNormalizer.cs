using System.Text.RegularExpressions;
using NugetMcp.Core.Models.Configuration;

namespace NugetMcp.Core.Services.CodeSimilarity.Simplifiers;

public class WhitespaceNormalizer : ICodeTextSimplify
{
    private readonly SimplifierConfiguration _config;
    private static readonly Regex MultipleWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex LineEndingRegex = new(@"\r\n|\r|\n", RegexOptions.Compiled);

    public WhitespaceNormalizer(SimplifierConfiguration config)
    {
        _config = config;
    }

    public int Priority => _config.Priority;

    public string Simplify(string codeText)
    {
        if (string.IsNullOrEmpty(codeText))
            return string.Empty;

        var normalized = LineEndingRegex.Replace(codeText, "\n");
        
        normalized = MultipleWhitespaceRegex.Replace(normalized, " ");
        
        return normalized.Trim();
    }
}