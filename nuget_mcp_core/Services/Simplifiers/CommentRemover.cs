using System.Text.RegularExpressions;
using NugetMcp.Core.Models.Configuration;

namespace NugetMcp.Core.Services.CodeSimilarity.Simplifiers;

public class CommentRemover : ICodeTextSimplify
{
    private readonly SimplifierConfiguration _config;
    private static readonly Regex SingleLineCommentRegex = new(@"//.*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MultiLineCommentRegex = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex XmlDocCommentRegex = new(@"///.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    public CommentRemover(SimplifierConfiguration config)
    {
        _config = config;
    }

    public int Priority => _config.Priority;

    public string Simplify(string codeText)
    {
        if (string.IsNullOrEmpty(codeText))
            return string.Empty;

        var result = codeText;

        // Remove XML documentation comments first (/// comments)
        result = XmlDocCommentRegex.Replace(result, string.Empty);
        
        // Remove single-line comments (// comments)
        result = SingleLineCommentRegex.Replace(result, string.Empty);
        
        // Remove multi-line comments (/* */ comments)
        result = MultiLineCommentRegex.Replace(result, string.Empty);

        return result;
    }
}