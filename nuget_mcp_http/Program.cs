using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NugetMcp.Core.Tools;
using NugetMcp.Core.Services;
using NugetMcp.Core.Models.Configuration;
using NugetMcp.Core.Services.CodeSimilarity;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Apply logging configuration from appsettings.json and environment variables
builder.Logging.SetMinimumLevel(LogLevel.Warning); // Default minimum
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);

// Allow configuration to override the defaults
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

builder.Services.AddSingleton<ICacheManager, FileCacheManager>();
builder.Services.AddSingleton<ISolutionLoader, RoslynSolutionLoader>();
builder.Services.AddSingleton<IPackageAssemblyResolver, NuGetPackageAssemblyResolver>();
builder.Services.AddSingleton<ISourceCodeReader, SourceCodeReader>();
builder.Services.AddSingleton<IUsageScanner, RoslynUsageScanner>();
builder.Services.AddSingleton<IParallelExecutor, TaskParallelExecutor>();
builder.Services.AddSingleton<IPackageUsageAnalyzer, PackageUsageAnalyzer>();

// Configure CodeSimilarity
builder.Services.Configure<CodeSimilarityConfiguration>(
    builder.Configuration.GetSection("CodeSimilarity"));
builder.Services.AddSingleton<ICodeSimilarityService, CodeSimilarityService>();

// Configure UsageTypeFilter
builder.Services.Configure<UsageTypeFilterConfiguration>(
    builder.Configuration.GetSection("UsageTypeFilter"));

// Configure MCP with HTTP transport
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(AnalyzePackageUsageTool).Assembly);

var app = builder.Build();

// Initialize tools
var analyzer = app.Services.GetRequiredService<IPackageUsageAnalyzer>();
AnalyzePackageUsageTool.Initialize(analyzer);
AnalyzeSymbolUsageTool.Initialize(analyzer);

// Map MCP endpoints
app.MapMcp();

// Get binding configuration from appsettings
var httpConfig = builder.Configuration.GetSection("HttpServer");
var ipAddress = httpConfig["IpAddress"] ?? "localhost";
var port = httpConfig["Port"] ?? "3001";
var url = $"http://{ipAddress}:{port}";

app.Run(url);