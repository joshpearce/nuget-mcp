using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NugetMcp.Core.Tools;
using NugetMcp.Core.Services;
using NugetMcp.Core.Extensions;

var builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    // For MCP servers, all logs must go to stderr
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Apply logging configuration from appsettings.json and environment variables
builder.Logging.SetMinimumLevel(LogLevel.Warning); // Default minimum
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);

// Allow configuration to override the defaults
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

builder.Services.AddNugetMcpCore(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(AnalyzePackageUsageTool).Assembly);

var host = builder.Build();

var analyzer = host.Services.GetRequiredService<IPackageUsageAnalyzer>();
AnalyzePackageUsageTool.Initialize(analyzer);
AnalyzeSymbolUsageTool.Initialize(analyzer);

await host.RunAsync();