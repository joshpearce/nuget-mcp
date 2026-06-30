using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NugetMcp.Core.Services;
using NugetMcp.Core.Extensions;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NugetUsageAnalyzerCli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("NuGet Package Usage Analyzer CLI")
        {
            CreateAnalyzeCommand(),
            CreateSymbolCommand(),
            CreateBatchCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    static Command CreateAnalyzeCommand()
    {
        var solutionPathOption = new Option<string>(
            "--solution",
            description: "Path to the .NET solution file (.sln)")
        {
            IsRequired = true
        };
        solutionPathOption.AddAlias("-s");

        var packageNameOption = new Option<string>(
            "--package",
            description: "Name of the NuGet package to analyze")
        {
            IsRequired = true
        };
        packageNameOption.AddAlias("-p");

        var packageVersionOption = new Option<string>(
            "--version",
            description: "Version of the NuGet package to analyze")
        {
            IsRequired = true
        };
        packageVersionOption.AddAlias("-v");

        var forceRefreshOption = new Option<bool>(
            "--force-refresh",
            description: "Force refresh of cached results")
        {
            IsRequired = false
        };
        forceRefreshOption.AddAlias("-f");

        var outputFormatOption = new Option<string>(
            "--output-format",
            description: "Output format: json, summary, detailed")
        {
            IsRequired = false
        };
        outputFormatOption.SetDefaultValue("summary");
        outputFormatOption.AddAlias("-o");

        var verboseOption = new Option<bool>(
            "--verbose",
            description: "Enable verbose logging")
        {
            IsRequired = false
        };

        var linesOption = new Option<int>(
            "--lines",
            description: "Number of context lines to include above and below each usage (default: 0)")
        {
            IsRequired = false
        };
        linesOption.SetDefaultValue(0);
        linesOption.AddAlias("-l");

        var analyzeCommand = new Command("analyze", "Analyze a single solution/package combination")
        {
            solutionPathOption,
            packageNameOption,
            packageVersionOption,
            forceRefreshOption,
            outputFormatOption,
            verboseOption,
            linesOption
        };

        analyzeCommand.SetHandler(async (string solutionPath, string packageName, string packageVersion, bool forceRefresh, string outputFormat, bool verbose, int lines) =>
        {
            await RunAnalysis(solutionPath, packageName, packageVersion, forceRefresh, outputFormat, verbose, lines);
        }, solutionPathOption, packageNameOption, packageVersionOption, forceRefreshOption, outputFormatOption, verboseOption, linesOption);

        return analyzeCommand;
    }

    static Command CreateSymbolCommand()
    {
        var solutionPathOption = new Option<string>(
            "--solution",
            description: "Path to the .NET solution file (.sln)")
        {
            IsRequired = true
        };
        solutionPathOption.AddAlias("-s");

        var symbolOption = new Option<string>(
            "--symbol",
            description: "Symbol to analyze (e.g., 'System.IO', 'System.Diagnostics.Stopwatch', 'Console.WriteLine')")
        {
            IsRequired = true
        };
        symbolOption.AddAlias("-x");

        var forceRefreshOption = new Option<bool>(
            "--force-refresh",
            description: "Force refresh of cached results")
        {
            IsRequired = false
        };
        forceRefreshOption.AddAlias("-f");

        var outputFormatOption = new Option<string>(
            "--output-format",
            description: "Output format: json, summary, detailed")
        {
            IsRequired = false
        };
        outputFormatOption.SetDefaultValue("summary");
        outputFormatOption.AddAlias("-o");

        var verboseOption = new Option<bool>(
            "--verbose",
            description: "Enable verbose logging")
        {
            IsRequired = false
        };

        var linesOption = new Option<int>(
            "--lines",
            description: "Number of context lines to include above and below each usage (default: 0)")
        {
            IsRequired = false
        };
        linesOption.SetDefaultValue(0);
        linesOption.AddAlias("-l");

        var symbolCommand = new Command("symbol", "Analyze symbol usage in a solution")
        {
            solutionPathOption,
            symbolOption,
            forceRefreshOption,
            outputFormatOption,
            verboseOption,
            linesOption
        };

        symbolCommand.SetHandler(async (string solutionPath, string targetSymbol, bool forceRefresh, string outputFormat, bool verbose, int lines) =>
        {
            await RunSymbolAnalysis(solutionPath, targetSymbol, forceRefresh, outputFormat, verbose, lines);
        }, solutionPathOption, symbolOption, forceRefreshOption, outputFormatOption, verboseOption, linesOption);

        return symbolCommand;
    }

    static Command CreateBatchCommand()
    {
        var configFileOption = new Option<string>(
            "--config",
            description: "Path to the batch configuration file (JSON)")
        {
            IsRequired = true
        };
        configFileOption.AddAlias("-c");

        var verboseOption = new Option<bool>(
            "--verbose",
            description: "Enable verbose logging")
        {
            IsRequired = false
        };

        var batchCommand = new Command("batch", "Analyze multiple solution/package combinations from a configuration file")
        {
            configFileOption,
            verboseOption
        };

        batchCommand.SetHandler(async (string configFile, bool verbose) =>
        {
            await RunBatchAnalysis(configFile, verbose);
        }, configFileOption, verboseOption);

        return batchCommand;
    }

    static async Task RunAnalysis(string solutionPath, string packageName, string packageVersion, bool forceRefresh, string outputFormat, bool verbose, int lines = 0)
    {
        var host = CreateHost(verbose);
        var analyzer = host.Services.GetRequiredService<IPackageUsageAnalyzer>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Starting analysis of {PackageName}@{PackageVersion} in {SolutionPath}",
                packageName, packageVersion, solutionPath);

            var result = await analyzer.AnalyzeAsync(solutionPath, packageName, packageVersion, forceRefresh, lines);

            switch (outputFormat.ToLowerInvariant())
            {
                case "json":
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Converters = { new JsonStringEnumConverter() }
                    }));
                    break;
                case "detailed":
                    OutputDetailedResults(result);
                    break;
                case "summary":
                default:
                    OutputSummaryResults(result);
                    break;
            }

            logger.LogInformation("Analysis completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analysis failed");
            Environment.ExitCode = 1;
        }
    }

    static async Task RunSymbolAnalysis(string solutionPath, string targetSymbol, bool forceRefresh, string outputFormat, bool verbose, int lines = 0)
    {
        var host = CreateHost(verbose);
        var analyzer = host.Services.GetRequiredService<IPackageUsageAnalyzer>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Starting symbol analysis of {TargetSymbol} in {SolutionPath}",
                targetSymbol, solutionPath);

            var result = await analyzer.AnalyzeSymbolAsync(solutionPath, targetSymbol, forceRefresh, lines);

            switch (outputFormat.ToLowerInvariant())
            {
                case "json":
                    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions 
                    { 
                        WriteIndented = true,
                        Converters = { new JsonStringEnumConverter() }
                    }));
                    break;
                case "detailed":
                    OutputDetailedSymbolResults(result, targetSymbol);
                    break;
                case "summary":
                default:
                    OutputSummarySymbolResults(result, targetSymbol);
                    break;
            }

            logger.LogInformation("Symbol analysis completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Symbol analysis failed");
            Environment.ExitCode = 1;
        }
    }

    static async Task RunBatchAnalysis(string configFile, bool verbose)
    {
        var host = CreateHost(verbose);
        var analyzer = host.Services.GetRequiredService<IPackageUsageAnalyzer>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Loading batch configuration from {ConfigFile}", configFile);

            var batchConfig = await LoadBatchConfiguration(configFile);
            logger.LogInformation("Loaded {JobCount} analysis jobs", batchConfig.Jobs.Count);

            var results = new List<BatchResult>();

            foreach (var job in batchConfig.Jobs)
            {
                logger.LogInformation("Processing job: {SolutionPath} -> {PackageName}@{PackageVersion}",
                    job.SolutionPath, job.PackageName, job.PackageVersion);

                try
                {
                    var result = await analyzer.AnalyzeAsync(job.SolutionPath, job.PackageName, job.PackageVersion, job.ForceRefresh);
                    results.Add(new BatchResult
                    {
                        Job = job,
                        Result = result,
                        Success = true
                    });
                    logger.LogInformation("Job completed successfully with {UsageCount} usages", result.TotalUsageCount);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Job failed");
                    results.Add(new BatchResult
                    {
                        Job = job,
                        Error = ex.Message,
                        Success = false
                    });
                }
            }

            OutputBatchResults(results);
            logger.LogInformation("Batch analysis completed. {SuccessCount}/{TotalCount} jobs succeeded",
                results.Count(r => r.Success), results.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Batch analysis failed");
            Environment.ExitCode = 1;
        }
    }

    static IHost CreateHost(bool verbose)
    {
        var builder = Host.CreateApplicationBuilder();
        
        // Clear default providers and configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        
        // Configure logging from environment variables and appsettings.json
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        
        // Override with verbose flag if specified
        if (verbose)
        {
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
        }

        builder.Services.AddNugetMcpCore(builder.Configuration);

        return builder.Build();
    }

    static async Task<BatchConfiguration> LoadBatchConfiguration(string configFile)
    {
        var jsonContent = await File.ReadAllTextAsync(configFile);
        var config = JsonSerializer.Deserialize<BatchConfiguration>(jsonContent);
        return config ?? throw new InvalidOperationException("Failed to deserialize batch configuration");
    }

    static void OutputSummaryResults(NugetMcp.Core.Models.AnalysisResult result)
    {
        Console.WriteLine("=== Package Usage Analysis Summary ===");
        Console.WriteLine($"Solution: {result.SolutionPath}");
        Console.WriteLine($"Package: {result.PackageName}@{result.PackageVersion}");
        Console.WriteLine($"Analysis Duration: {result.AnalysisDuration:hh\\:mm\\:ss\\.fff}");
        Console.WriteLine($"Total Usages: {result.TotalUsageCount}");
        Console.WriteLine($"Projects Analyzed: {result.AnalyzedProjects.Count}");
        
        if (result.Errors.Any())
        {
            Console.WriteLine($"Errors: {result.Errors.Count}");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        if (result.Usages.Any())
        {
            Console.WriteLine("\n=== Usage by Project ===");
            var usagesByProject = result.Usages.GroupBy(u => u.ProjectName);
            foreach (var projectGroup in usagesByProject)
            {
                Console.WriteLine($"{projectGroup.Key}: {projectGroup.Count()} usages");
                var usagesByType = projectGroup.GroupBy(u => u.Type);
                foreach (var typeGroup in usagesByType)
                {
                    Console.WriteLine($"  {typeGroup.Key}: {typeGroup.Count()}");
                }
            }
        }
    }

    static void OutputSummarySymbolResults(NugetMcp.Core.Models.AnalysisResult result, string targetSymbol)
    {
        Console.WriteLine("=== Symbol Usage Analysis Summary ===");
        Console.WriteLine($"Solution: {result.SolutionPath}");
        Console.WriteLine($"Symbol: {targetSymbol}");
        Console.WriteLine($"Analysis Duration: {result.AnalysisDuration:hh\\:mm\\:ss\\.fff}");
        Console.WriteLine($"Total Usages: {result.TotalUsageCount}");
        Console.WriteLine($"Projects Analyzed: {result.AnalyzedProjects.Count}");
        
        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"Errors: {result.Errors.Count}");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        if (result.Usages.Count > 0)
        {
            Console.WriteLine("\n=== Usage by Project ===");
            var usagesByProject = result.Usages.GroupBy(u => u.ProjectName);
            foreach (var projectGroup in usagesByProject)
            {
                Console.WriteLine($"{projectGroup.Key}: {projectGroup.Count()} usages");
                var usagesByType = projectGroup.GroupBy(u => u.Type);
                foreach (var typeGroup in usagesByType)
                {
                    Console.WriteLine($"  {typeGroup.Key}: {typeGroup.Count()}");
                }
            }
        }
    }

    static void OutputDetailedSymbolResults(NugetMcp.Core.Models.AnalysisResult result, string targetSymbol)
    {
        OutputSummarySymbolResults(result, targetSymbol);
        
        if (result.Usages.Count > 0)
        {
            Console.WriteLine("\n=== Detailed Usage Information ===");
            foreach (var usage in result.Usages.OrderBy(u => u.ProjectName).ThenBy(u => u.FilePath).ThenBy(u => u.StartLine))
            {
                Console.WriteLine($"\nProject: {usage.ProjectName}");
                Console.WriteLine($"File: {usage.FilePath}:{usage.StartLine}");
                Console.WriteLine($"Type: {usage.Type}");
                Console.WriteLine($"Symbol: {usage.SymbolName}");
                if (!string.IsNullOrEmpty(usage.Namespace))
                    Console.WriteLine($"Namespace: {usage.Namespace}");
                
                // Handle multi-line code context
                if (usage.HasContext && usage.CodeText.Contains(Environment.NewLine))
                {
                    Console.WriteLine($"Code (lines {usage.ContextStartLine}-{usage.ContextEndLine}):");
                    var lines = usage.CodeText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var lineNumber = usage.ContextStartLine + i;
                        var marker = (lineNumber >= usage.StartLine && lineNumber <= usage.EndLine) ? ">>>" : "   ";
                        Console.WriteLine($"{marker} {lineNumber,4}: {lines[i]}");
                    }
                }
                else
                {
                    Console.WriteLine($"Code: {usage.CodeText}");
                }
            }
        }
    }

    static void OutputDetailedResults(NugetMcp.Core.Models.AnalysisResult result)
    {
        OutputSummaryResults(result);
        
        if (result.Usages.Any())
        {
            Console.WriteLine("\n=== Detailed Usage Information ===");
            foreach (var usage in result.Usages.OrderBy(u => u.ProjectName).ThenBy(u => u.FilePath).ThenBy(u => u.StartLine))
            {
                Console.WriteLine($"\nProject: {usage.ProjectName}");
                Console.WriteLine($"File: {usage.FilePath}:{usage.StartLine}");
                Console.WriteLine($"Type: {usage.Type}");
                Console.WriteLine($"Symbol: {usage.SymbolName}");
                if (!string.IsNullOrEmpty(usage.Namespace))
                    Console.WriteLine($"Namespace: {usage.Namespace}");
                
                // Handle multi-line code context
                if (usage.HasContext && usage.CodeText.Contains(Environment.NewLine))
                {
                    Console.WriteLine($"Code (lines {usage.ContextStartLine}-{usage.ContextEndLine}):");
                    var lines = usage.CodeText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var lineNumber = usage.ContextStartLine + i;
                        var marker = (lineNumber >= usage.StartLine && lineNumber <= usage.EndLine) ? ">>>" : "   ";
                        Console.WriteLine($"{marker} {lineNumber,4}: {lines[i]}");
                    }
                }
                else
                {
                    Console.WriteLine($"Code: {usage.CodeText}");
                }
            }
        }
    }

    static void OutputBatchResults(List<BatchResult> results)
    {
        Console.WriteLine("=== Batch Analysis Results ===");
        Console.WriteLine($"Total Jobs: {results.Count}");
        Console.WriteLine($"Successful: {results.Count(r => r.Success)}");
        Console.WriteLine($"Failed: {results.Count(r => !r.Success)}");

        foreach (var result in results)
        {
            Console.WriteLine($"\n{result.Job.SolutionPath} -> {result.Job.PackageName}@{result.Job.PackageVersion}");
            if (result.Success && result.Result != null)
            {
                Console.WriteLine($"  ✓ Success: {result.Result.TotalUsageCount} usages found");
            }
            else
            {
                Console.WriteLine($"  ✗ Failed: {result.Error}");
            }
        }
    }
}

public class BatchConfiguration
{
    public List<AnalysisJob> Jobs { get; set; } = new();
}

public class AnalysisJob
{
    public required string SolutionPath { get; set; }
    public required string PackageName { get; set; }
    public required string PackageVersion { get; set; }
    public bool ForceRefresh { get; set; } = false;
}

public class BatchResult
{
    public required AnalysisJob Job { get; set; }
    public NugetMcp.Core.Models.AnalysisResult? Result { get; set; }
    public string? Error { get; set; }
    public bool Success { get; set; }
}
