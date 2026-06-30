# NuGet Usage Analyzer

Last verified: 2026-06-30

## Tech Stack
- Language/Runtime: C# / .NET 8 (`net8.0`), nullable + implicit usings enabled
- Analysis: Roslyn (`Microsoft.CodeAnalysis.CSharp.Workspaces`, `.Workspaces.MSBuild`) via `MSBuildWorkspace`; requires `Microsoft.Build.Locator`
- NuGet APIs: `NuGet.Protocol` / `NuGet.Packaging` / `NuGet.Configuration` / `NuGet.ProjectModel` (6.14.0)
- MCP: `ModelContextProtocol` 0.3.0-preview.2 (core); `ModelContextProtocol.AspNetCore` (HTTP)
- DI/Hosting/Config/Logging: `Microsoft.Extensions.*` 9.x; CLI uses `System.CommandLine` 2.0 beta
- Misc: `CsvHelper`, `System.Text.Json`

## Commands
- `dotnet build nuget_mcp.sln` — build all four projects
- `dotnet run --project nuget_mcp_stdio` — MCP server over stdio
- `dotnet run --project nuget_mcp_http` — MCP server over HTTP (localhost:3001)
- `dotnet run --project nuget_usage_analyzer_cli -- analyze -s <sln> -p <pkg> -v <ver>` — CLI package analysis
- `dotnet run --project nuget_usage_analyzer_cli -- symbol -s <sln> -x <symbol>` — CLI symbol analysis
- `dotnet run --project nuget_usage_analyzer_cli -- batch -c <config.json>` — CLI batch mode

## Architecture
One engine, three frontends. `nuget_mcp_core` holds all logic; the three runnable projects are
thin transports that build the **same DI graph** and call the same `IPackageUsageAnalyzer`.

- `nuget_mcp_core/` — analysis engine + MCP tool definitions. See its own CLAUDE.md for contracts.
- `nuget_mcp_stdio/` — `Host` app, `WithStdioServerTransport()`. Logs to **stderr** (MCP requirement).
- `nuget_mcp_http/` — `WebApplication`, `WithHttpTransport()` + `MapMcp()`, binds from `HttpServer` config.
- `nuget_usage_analyzer_cli/` — `System.CommandLine` root with `analyze` / `symbol` / `batch` commands.

The DI registration block is duplicated verbatim across all three `Program.cs` files (plus the
CLI's `CreateHost`). When you add or change a core service registration, update **all three**.

## Conventions
- Core code namespace root: `NugetMcp.Core` (note: dir is `nuget_mcp_core`, namespace is PascalCase).
- All core services are interface-backed (`ISolutionLoader`, `IUsageScanner`, `ICacheManager`, …)
  and registered as singletons. Prefer adding behavior behind a new interface + registration.
- Config sections (`CodeSimilarity`, `UsageTypeFilter`, `HttpServer`, `Logging`) live in each
  runnable project's `appsettings.json` (CopyToOutputDirectory). Keep the shared sections in sync.

## Boundaries
- Safe to edit: all four project source trees.
- Do not commit secrets; there are none today and none are needed (all analysis is local).
- `analyze_symbol_usage_flow.md` is design documentation — keep it conceptual, not a code mirror.

## Notes
- This working tree is **not a git repository**; documentation changes here cannot be committed.
