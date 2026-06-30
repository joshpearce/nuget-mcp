using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using NugetMcp.Core.Models;
using System.Collections.Concurrent;

namespace NugetMcp.Core.Services;

public class RoslynUsageScanner : IUsageScanner
{
    private readonly ILogger<RoslynUsageScanner> _logger;
    private readonly ISourceCodeReader _sourceCodeReader;

    public RoslynUsageScanner(ILogger<RoslynUsageScanner> logger, ISourceCodeReader sourceCodeReader)
    {
        _logger = logger;
        _sourceCodeReader = sourceCodeReader;
    }

    public async Task<List<PackageUsageInstance>> ScanProjectAsync(Project project, HashSet<string> targetAssemblies, int contextLines = 0)
    {
        _logger.LogDebug("Scanning project {ProjectName} for assemblies: {TargetAssemblies}", 
            project.Name, string.Join(", ", targetAssemblies));

        var usages = new ConcurrentBag<PackageUsageInstance>();
        var compilation = await project.GetCompilationAsync();

        if (compilation == null)
        {
            _logger.LogWarning("Could not get compilation for project {ProjectName}", project.Name);
            return new List<PackageUsageInstance>();
        }

        var tasks = project.Documents.Select(async document =>
        {
            try
            {
                var documentUsages = await ScanDocumentAsync(document, compilation, targetAssemblies, project.Name, contextLines);
                foreach (var usage in documentUsages)
                {
                    usages.Add(usage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan document {DocumentName} in project {ProjectName}", 
                    document.Name, project.Name);
            }
        });

        await Task.WhenAll(tasks);

        var result = usages.ToList();
        _logger.LogInformation("Found {UsageCount} usages in project {ProjectName}", result.Count, project.Name);
        
        return result;
    }

    public async Task<List<PackageUsageInstance>> ScanProjectForSymbolsAsync(Project project, HashSet<string> targetSymbols, int contextLines = 0)
    {
        _logger.LogDebug("Scanning project {ProjectName} for symbols: {TargetSymbols}", 
            project.Name, string.Join(", ", targetSymbols));

        var usages = new ConcurrentBag<PackageUsageInstance>();
        var compilation = await project.GetCompilationAsync();

        if (compilation == null)
        {
            _logger.LogWarning("Could not get compilation for project {ProjectName}", project.Name);
            return new List<PackageUsageInstance>();
        }

        var tasks = project.Documents.Select(async document =>
        {
            try
            {
                var documentUsages = await ScanDocumentForSymbolsAsync(document, compilation, targetSymbols, project.Name, contextLines);
                foreach (var usage in documentUsages)
                {
                    usages.Add(usage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scan document {DocumentName} in project {ProjectName}", 
                    document.Name, project.Name);
            }
        });

        await Task.WhenAll(tasks);

        var result = usages.ToList();
        _logger.LogInformation("Found {UsageCount} symbol usages in project {ProjectName}", result.Count, project.Name);
        
        return result;
    }

    private async Task<List<PackageUsageInstance>> ScanDocumentAsync(
        Document document, 
        Compilation compilation, 
        HashSet<string> targetAssemblies, 
        string projectName,
        int contextLines)
    {
        var usages = new List<PackageUsageInstance>();
        
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            return usages;
            
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        
        if (syntaxTree.GetRoot() is not CompilationUnitSyntax root)
            return usages;

        var walker = new PackageUsageWalker(semanticModel, targetAssemblies, projectName, document.FilePath ?? document.Name, _sourceCodeReader, contextLines);
        walker.Visit(root);
        
        return await walker.GetUsagesAsync();
    }

    private async Task<List<PackageUsageInstance>> ScanDocumentForSymbolsAsync(
        Document document, 
        Compilation compilation, 
        HashSet<string> targetSymbols, 
        string projectName,
        int contextLines)
    {
        var usages = new List<PackageUsageInstance>();
        
        var syntaxTree = await document.GetSyntaxTreeAsync();
        if (syntaxTree == null)
            return usages;
            
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        
        if (syntaxTree.GetRoot() is not CompilationUnitSyntax root)
            return usages;

        var walker = new SymbolUsageWalker(semanticModel, targetSymbols, projectName, document.FilePath ?? document.Name, _sourceCodeReader, contextLines);
        walker.Visit(root);
        
        return await walker.GetUsagesAsync();
    }

    private class PackageUsageWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly HashSet<string> _targetAssemblies;
        private readonly string _projectName;
        private readonly string _filePath;
        private readonly ISourceCodeReader _sourceCodeReader;
        private readonly int _contextLines;
        private readonly List<PackageUsageInstance> _usages = new();

        public PackageUsageWalker(SemanticModel semanticModel, HashSet<string> targetAssemblies, string projectName, string filePath, ISourceCodeReader sourceCodeReader, int contextLines)
        {
            _semanticModel = semanticModel;
            _targetAssemblies = targetAssemblies;
            _projectName = projectName;
            _filePath = filePath;
            _sourceCodeReader = sourceCodeReader;
            _contextLines = contextLines;
        }

        public async Task<List<PackageUsageInstance>> GetUsagesAsync()
        {
            if (_contextLines > 0)
            {
                await PopulateContextForAllUsagesAsync();
            }
            return _usages;
        }

        public List<PackageUsageInstance> GetUsages() => _usages;

        public override void VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name != null)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node.Name);
                if (symbolInfo.Symbol is INamespaceSymbol namespaceSymbol)
                {
                    CheckAndAddUsage(node, namespaceSymbol, UsageType.UsingDirective, namespaceSymbol.Name);
                }
            }
            base.VisitUsingDirective(node);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                var usageType = DetermineUsageType(node);
                CheckAndAddUsage(node, symbolInfo.Symbol, usageType, symbolInfo.Symbol.Name);
            }
            base.VisitIdentifierName(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                CheckAndAddUsage(node, symbolInfo.Symbol, UsageType.TypeReference, symbolInfo.Symbol.Name);
            }

            foreach (var typeArgument in node.TypeArgumentList.Arguments)
            {
                var typeSymbolInfo = _semanticModel.GetSymbolInfo(typeArgument);
                if (typeSymbolInfo.Symbol != null)
                {
                    CheckAndAddUsage(typeArgument, typeSymbolInfo.Symbol, UsageType.GenericTypeArgument, typeSymbolInfo.Symbol.Name);
                }
            }

            base.VisitGenericName(node);
        }

        public override void VisitQualifiedName(QualifiedNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                CheckAndAddUsage(node, symbolInfo.Symbol, UsageType.TypeReference, symbolInfo.Symbol.Name);
            }
            base.VisitQualifiedName(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                var usageType = symbolInfo.Symbol.Kind switch
                {
                    SymbolKind.Method => UsageType.MethodInvocation,
                    SymbolKind.Property => UsageType.PropertyAccess,
                    SymbolKind.Field => UsageType.FieldAccess,
                    SymbolKind.Event => UsageType.EventReference,
                    _ => UsageType.Direct
                };
                CheckAndAddUsage(node, symbolInfo.Symbol, usageType, symbolInfo.Symbol.Name);
            }
            base.VisitMemberAccessExpression(node);
        }

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var typeSymbolInfo = _semanticModel.GetSymbolInfo(node.Type);
            if (typeSymbolInfo.Symbol != null)
            {
                CheckAndAddUsage(node.Type, typeSymbolInfo.Symbol, UsageType.VariableDeclaration, typeSymbolInfo.Symbol.Name);
            }
            base.VisitVariableDeclaration(node);
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            if (node.Type != null)
            {
                var typeSymbolInfo = _semanticModel.GetSymbolInfo(node.Type);
                if (typeSymbolInfo.Symbol != null)
                {
                    CheckAndAddUsage(node.Type, typeSymbolInfo.Symbol, UsageType.ParameterType, typeSymbolInfo.Symbol.Name);
                }
            }
            base.VisitParameter(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var returnTypeSymbolInfo = _semanticModel.GetSymbolInfo(node.ReturnType);
            if (returnTypeSymbolInfo.Symbol != null)
            {
                CheckAndAddUsage(node.ReturnType, returnTypeSymbolInfo.Symbol, UsageType.ReturnType, returnTypeSymbolInfo.Symbol.Name);
            }
            base.VisitMethodDeclaration(node);
        }

        public override void VisitBaseList(BaseListSyntax node)
        {
            foreach (var baseType in node.Types)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(baseType.Type);
                if (symbolInfo.Symbol != null)
                {
                    CheckAndAddUsage(baseType.Type, symbolInfo.Symbol, UsageType.Inheritance, symbolInfo.Symbol.Name);
                }
            }
            base.VisitBaseList(node);
        }

        public override void VisitAttributeList(AttributeListSyntax node)
        {
            foreach (var attribute in node.Attributes)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(attribute);
                if (symbolInfo.Symbol != null)
                {
                    CheckAndAddUsage(attribute, symbolInfo.Symbol, UsageType.Attribute, symbolInfo.Symbol.Name);
                }
            }
            base.VisitAttributeList(node);
        }

        private UsageType DetermineUsageType(SyntaxNode node)
        {
            return node.Parent switch
            {
                VariableDeclarationSyntax => UsageType.VariableDeclaration,
                InvocationExpressionSyntax => UsageType.MethodInvocation,
                MemberAccessExpressionSyntax => UsageType.PropertyAccess,
                _ => UsageType.Direct
            };
        }

        private void CheckAndAddUsage(SyntaxNode node, ISymbol symbol, UsageType usageType, string symbolName)
        {
            var assemblyName = GetAssemblyName(symbol);
            if (assemblyName != null && _targetAssemblies.Contains(assemblyName))
            {
                var lineSpan = node.GetLocation().GetLineSpan();
                var usage = new PackageUsageInstance
                {
                    ProjectName = _projectName,
                    FilePath = _filePath,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    CodeText = node.ToString(),
                    Type = usageType,
                    SymbolName = symbolName,
                    Namespace = symbol.ContainingNamespace?.ToDisplayString(),
                    AssemblyName = assemblyName
                };

                _usages.Add(usage);
            }
        }

        private string? GetAssemblyName(ISymbol symbol)
        {
            var assembly = symbol.ContainingAssembly;
            return assembly?.Name;
        }

        private async Task PopulateContextForAllUsagesAsync()
        {
            foreach (var usage in _usages)
            {
                try
                {
                    var snippet = await _sourceCodeReader.ReadCodeSnippetAsync(_filePath, usage.StartLine, _contextLines);
                    usage.CodeText = snippet.Content;
                    usage.ContextStartLine = snippet.ActualStartLine;
                    usage.ContextEndLine = snippet.ActualEndLine;
                    usage.HasContext = true;
                }
                catch (Exception)
                {
                    // If context reading fails, keep original CodeText
                    usage.ContextStartLine = usage.StartLine;
                    usage.ContextEndLine = usage.EndLine;
                    usage.HasContext = false;
                }
            }
        }
    }

    private class SymbolUsageWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly HashSet<string> _targetSymbols;
        private readonly string _projectName;
        private readonly string _filePath;
        private readonly ISourceCodeReader _sourceCodeReader;
        private readonly int _contextLines;
        private readonly List<PackageUsageInstance> _usages = new();

        public SymbolUsageWalker(SemanticModel semanticModel, HashSet<string> targetSymbols, string projectName, string filePath, ISourceCodeReader sourceCodeReader, int contextLines)
        {
            _semanticModel = semanticModel;
            _targetSymbols = targetSymbols;
            _projectName = projectName;
            _filePath = filePath;
            _sourceCodeReader = sourceCodeReader;
            _contextLines = contextLines;
        }

        public async Task<List<PackageUsageInstance>> GetUsagesAsync()
        {
            if (_contextLines > 0)
            {
                await PopulateContextForAllUsagesAsync();
            }
            return _usages;
        }

        public List<PackageUsageInstance> GetUsages() => _usages;

        public override void VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name != null)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node.Name);
                if (symbolInfo.Symbol is INamespaceSymbol namespaceSymbol)
                {
                    CheckAndAddSymbolUsage(node, namespaceSymbol, UsageType.UsingDirective, namespaceSymbol.Name);
                }
            }
            base.VisitUsingDirective(node);
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                var usageType = DetermineUsageType(node);
                CheckAndAddSymbolUsage(node, symbolInfo.Symbol, usageType, symbolInfo.Symbol.Name);
            }
            base.VisitIdentifierName(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                CheckAndAddSymbolUsage(node, symbolInfo.Symbol, UsageType.TypeReference, symbolInfo.Symbol.Name);
            }

            foreach (var typeArgument in node.TypeArgumentList.Arguments)
            {
                var typeSymbolInfo = _semanticModel.GetSymbolInfo(typeArgument);
                if (typeSymbolInfo.Symbol != null)
                {
                    CheckAndAddSymbolUsage(typeArgument, typeSymbolInfo.Symbol, UsageType.GenericTypeArgument, typeSymbolInfo.Symbol.Name);
                }
            }

            base.VisitGenericName(node);
        }

        public override void VisitQualifiedName(QualifiedNameSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                CheckAndAddSymbolUsage(node, symbolInfo.Symbol, UsageType.TypeReference, symbolInfo.Symbol.Name);
            }
            base.VisitQualifiedName(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol != null)
            {
                var usageType = symbolInfo.Symbol.Kind switch
                {
                    SymbolKind.Method => UsageType.MethodInvocation,
                    SymbolKind.Property => UsageType.PropertyAccess,
                    SymbolKind.Field => UsageType.FieldAccess,
                    SymbolKind.Event => UsageType.EventReference,
                    _ => UsageType.Direct
                };
                CheckAndAddSymbolUsage(node, symbolInfo.Symbol, usageType, symbolInfo.Symbol.Name);
            }
            base.VisitMemberAccessExpression(node);
        }

        public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        {
            var typeSymbolInfo = _semanticModel.GetSymbolInfo(node.Type);
            if (typeSymbolInfo.Symbol != null)
            {
                CheckAndAddSymbolUsage(node.Type, typeSymbolInfo.Symbol, UsageType.VariableDeclaration, typeSymbolInfo.Symbol.Name);
            }
            base.VisitVariableDeclaration(node);
        }

        public override void VisitParameter(ParameterSyntax node)
        {
            if (node.Type != null)
            {
                var typeSymbolInfo = _semanticModel.GetSymbolInfo(node.Type);
                if (typeSymbolInfo.Symbol != null)
                {
                    CheckAndAddSymbolUsage(node.Type, typeSymbolInfo.Symbol, UsageType.ParameterType, typeSymbolInfo.Symbol.Name);
                }
            }
            base.VisitParameter(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var returnTypeSymbolInfo = _semanticModel.GetSymbolInfo(node.ReturnType);
            if (returnTypeSymbolInfo.Symbol != null)
            {
                CheckAndAddSymbolUsage(node.ReturnType, returnTypeSymbolInfo.Symbol, UsageType.ReturnType, returnTypeSymbolInfo.Symbol.Name);
            }
            base.VisitMethodDeclaration(node);
        }

        public override void VisitBaseList(BaseListSyntax node)
        {
            foreach (var baseType in node.Types)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(baseType.Type);
                if (symbolInfo.Symbol != null)
                {
                    CheckAndAddSymbolUsage(baseType.Type, symbolInfo.Symbol, UsageType.Inheritance, symbolInfo.Symbol.Name);
                }
            }
            base.VisitBaseList(node);
        }

        public override void VisitAttributeList(AttributeListSyntax node)
        {
            foreach (var attribute in node.Attributes)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(attribute);
                if (symbolInfo.Symbol != null)
                {
                    CheckAndAddSymbolUsage(attribute, symbolInfo.Symbol, UsageType.Attribute, symbolInfo.Symbol.Name);
                }
            }
            base.VisitAttributeList(node);
        }

        private UsageType DetermineUsageType(SyntaxNode node)
        {
            return node.Parent switch
            {
                VariableDeclarationSyntax => UsageType.VariableDeclaration,
                InvocationExpressionSyntax => UsageType.MethodInvocation,
                MemberAccessExpressionSyntax => UsageType.PropertyAccess,
                _ => UsageType.Direct
            };
        }

        private void CheckAndAddSymbolUsage(SyntaxNode node, ISymbol symbol, UsageType usageType, string symbolName)
        {
            if (IsSymbolMatch(symbol))
            {
                var lineSpan = node.GetLocation().GetLineSpan();
                var usage = new PackageUsageInstance
                {
                    ProjectName = _projectName,
                    FilePath = _filePath,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    CodeText = node.ToString(),
                    Type = usageType,
                    SymbolName = symbolName,
                    Namespace = symbol.ContainingNamespace?.ToDisplayString(),
                    AssemblyName = symbol.ContainingAssembly?.Name
                };

                _usages.Add(usage);
            }
        }

        private bool IsSymbolMatch(ISymbol symbol)
        {
            foreach (var targetSymbol in _targetSymbols)
            {
                // 1. Check exact namespace match (existing behavior)
                var symbolNamespace = symbol.ContainingNamespace?.ToDisplayString();
                if (symbolNamespace == targetSymbol) return true;
                
                // 2. Check fully qualified type/member name
                var fullName = symbol.ToDisplayString();
                if (fullName == targetSymbol) return true;
                
                // 3. Check namespace prefix match (existing behavior)
                if (symbolNamespace?.StartsWith(targetSymbol + ".", StringComparison.OrdinalIgnoreCase) == true) return true;
            }
            return false;
        }

        private async Task PopulateContextForAllUsagesAsync()
        {
            foreach (var usage in _usages)
            {
                try
                {
                    var snippet = await _sourceCodeReader.ReadCodeSnippetAsync(_filePath, usage.StartLine, _contextLines);
                    usage.CodeText = snippet.Content;
                    usage.ContextStartLine = snippet.ActualStartLine;
                    usage.ContextEndLine = snippet.ActualEndLine;
                    usage.HasContext = true;
                }
                catch (Exception)
                {
                    // If context reading fails, keep original CodeText
                    usage.ContextStartLine = usage.StartLine;
                    usage.ContextEndLine = usage.EndLine;
                    usage.HasContext = false;
                }
            }
        }
    }
}