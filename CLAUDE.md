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
- `dotnet build nuget_mcp.sln` — build all five projects
- `dotnet run --project nuget_mcp_stdio` — MCP server over stdio
- `dotnet run --project nuget_mcp_http` — MCP server over HTTP (localhost:3001)
- `dotnet run --project nuget_usage_analyzer_cli -- analyze -s <sln> -p <pkg> -v <ver>` — CLI package analysis
- `dotnet run --project nuget_usage_analyzer_cli -- symbol -s <sln> -x <symbol>` — CLI symbol analysis
- `dotnet run --project nuget_usage_analyzer_cli -- batch -c <config.json>` — CLI batch mode
- `git submodule update --init --recursive` — populate `test-fixtures/` (required before running integration tests)
- `dotnet test nuget_mcp_core.IntegrationTests` — real-repo integration tests against vendored fixtures (see `test-fixtures/NOTES.md`)
- `scripts/vet-fixtures.sh` — automated dangerous-pattern guard for `test-fixtures/`; re-run on every fixture pin bump

## Architecture
One engine, three frontends, plus an integration test project. `nuget_mcp_core` holds all logic;
the three runnable projects are thin transports that build the **same DI graph** (via
`AddNugetMcpCore`) and call the same `IPackageUsageAnalyzer`.

- `nuget_mcp_core/` — analysis engine + MCP tool definitions. See its own CLAUDE.md for contracts.
- `nuget_mcp_stdio/` — `Host` app, `WithStdioServerTransport()`. Logs to **stderr** (MCP requirement).
- `nuget_mcp_http/` — `WebApplication`, `WithHttpTransport()` + `MapMcp()`, binds from `HttpServer` config.
- `nuget_usage_analyzer_cli/` — `System.CommandLine` root with `analyze` / `symbol` / `batch` commands.
- `nuget_mcp_core.IntegrationTests/` — xUnit tests running the real analyzer against two pinned,
  security-vetted git submodule fixtures (`test-fixtures/restsharp`, `test-fixtures/eshoponweb`).
  Shares one `AnalyzerFixture`-built DI graph per xUnit collection. See `test-fixtures/NOTES.md`
  for fixture provenance, vetting results, and the package/symbol facts the tests assert on.

DI registration lives in one place: `nuget_mcp_core/Extensions/ServiceCollectionExtensions.cs`
(`AddNugetMcpCore(IServiceCollection, IConfiguration)`). All three frontends and the integration
test project call it instead of duplicating the service list — add or change a core service
registration there once.

## Conventions
- Core code namespace root: `NugetMcp.Core` (note: dir is `nuget_mcp_core`, namespace is PascalCase).
- All core services are interface-backed (`ISolutionLoader`, `IUsageScanner`, `ICacheManager`, …)
  and registered as singletons. Prefer adding behavior behind a new interface + registration.
- Config sections (`CodeSimilarity`, `UsageTypeFilter`, `HttpServer`, `Logging`) live in each
  runnable project's `appsettings.json` (CopyToOutputDirectory). Keep the shared sections in sync.

## Boundaries
- Safe to edit: all five project source trees, plus `scripts/` and `test-fixtures/NOTES.md`.
- `test-fixtures/restsharp` and `test-fixtures/eshoponweb` are vendored git submodules pinned to
  specific commits — don't edit their contents in place; re-vet and re-pin deliberately instead
  (see `test-fixtures/NOTES.md`).
- Do not commit secrets; there are none today and none are needed (all analysis is local).
- `analyze_symbol_usage_flow.md` is design documentation — keep it conceptual, not a code mirror.

## Notes
- This is a git repository with an `origin/main` remote — standard git workflow applies.
