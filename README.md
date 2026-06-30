# NuGet Usage Analyzer

A .NET 8 toolset that analyzes how a NuGet package — or any individual namespace, type, or
member symbol — is actually used across a C# solution. It uses Roslyn semantic analysis (not
text matching) to find real usages, then reports each one with file, line, usage type, and
optional surrounding code context.

Typical uses: dependency auditing, dead/unused-API detection, and migration impact analysis
(e.g. "if I drop or upgrade this package, what breaks and where?").

The same analysis engine is exposed three ways: as an MCP server over **stdio**, as an MCP
server over **HTTP**, and as a standalone **CLI**.

## Projects

| Project | SDK / Type | Role |
|---------|-----------|------|
| `nuget_mcp_core` | class library | The analysis engine: Roslyn solution/usage scanning, NuGet assembly resolution, similarity de-duplication, caching, and the two MCP tool definitions. All other projects depend on it. |
| `nuget_mcp_stdio` | console Exe | MCP server over **stdio** — the usual transport for local MCP clients (Claude Code/Desktop). |
| `nuget_mcp_http` | ASP.NET Core Web | MCP server over **HTTP** (`ModelContextProtocol.AspNetCore`). Binds `localhost:3001` by default. |
| `nuget_usage_analyzer_cli` | console Exe | Standalone CLI (`System.CommandLine`) for running the same analysis from a terminal or scripts, including batch mode. |
| `nuget_mcp_core.IntegrationTests` | xUnit test project | Runs the real analyzer against two pinned, vetted real-world OSS fixtures. See [Testing](#testing). |

The two MCP servers expose identical tools (`analyze_package_usage`, `analyze_symbol_usage`);
they differ only in transport. The CLI wraps the same engine with `analyze`, `symbol`, and
`batch` commands.

## Requirements

- .NET 8 SDK
- The **MSBuild / .NET SDK toolchain must be installed and on PATH**. The analyzer loads target
  solutions with `MSBuildWorkspace` and shells out to `dotnet restore` / `dotnet nuget locals`
  to resolve package assemblies. Target solutions are analyzed against your machine's NuGet
  global packages folder.

## Build

```bash
dotnet build nuget_mcp.sln
```

## Running each entry point

### CLI (`nuget_usage_analyzer_cli`)

```bash
# Analyze a single package in a solution
dotnet run --project nuget_usage_analyzer_cli -- analyze \
  --solution /path/to/Target.sln --package Newtonsoft.Json --version 13.0.3 \
  --output-format detailed --lines 2

# Analyze a symbol (namespace, type, or member)
dotnet run --project nuget_usage_analyzer_cli -- symbol \
  --solution /path/to/Target.sln --symbol System.Diagnostics.Stopwatch

# Batch mode: many solution/package jobs from a config file
dotnet run --project nuget_usage_analyzer_cli -- batch --config sample-batch-config.json
```

Common options: `--force-refresh/-f` (bypass cache), `--output-format/-o` (`summary` |
`detailed` | `json`, default `summary`), `--lines/-l` (context lines), `--verbose`.
See `nuget_usage_analyzer_cli/sample-batch-config.json` for the batch config shape (a list of
`{ solutionPath, packageName, packageVersion, forceRefresh }` jobs).

### MCP server over stdio (`nuget_mcp_stdio`)

```bash
dotnet run --project nuget_mcp_stdio
```

Speaks MCP over stdin/stdout; all logs go to stderr. Point a local MCP client at this executable.

### MCP server over HTTP (`nuget_mcp_http`)

```bash
dotnet run --project nuget_mcp_http
```

Serves MCP at `http://localhost:3001` by default (change via the `HttpServer` section of
`appsettings.json`).

## MCP tools

Both servers expose:

- **`analyze_package_usage`** — `(solutionPath, packageName, packageVersion, contextLines=0)` →
  JSON report of every usage of the package's assemblies in the solution.
- **`analyze_symbol_usage`** — `(solutionPath, targetSymbol, contextLines=0)` → JSON report of
  every usage of a namespace/type/member (e.g. `System.IO`, `Console.WriteLine`).

## Configuration (`appsettings.json`)

Each runnable project ships an `appsettings.json` (copied to output). Shared sections:

- **`CodeSimilarity`** — de-duplicates near-identical usage snippets. Configurable simplifiers
  (`WhitespaceNormalizer`, `CommentRemover`) and comparisons (`LevenshteinDistanceComparison`
  with `MaxDistance` / `ThresholdPercent`). Set `Enabled: false` to report every raw usage.
- **`UsageTypeFilter.IncludedUsageTypes`** — restricts results to specific usage kinds
  (e.g. `MethodInvocation`, `PropertyAccess`, `TypeReference`). Empty/absent = include all.
- **`Logging`** — standard `Microsoft.Extensions.Logging` configuration.
- **`HttpServer`** (HTTP project only) — `IpAddress` and `Port` for the bind URL.

## Caching

Analysis results are cached as JSON under your system temp directory in
`nuget-usage-analysis-cache/`. The cache key includes the target solution's last-write time, so
editing and saving the `.sln` file invalidates stale results; `--force-refresh` (CLI) bypasses
the cache explicitly.

## Testing

`nuget_mcp_core.IntegrationTests` runs the real `IPackageUsageAnalyzer` against two real-world
OSS .NET repos, vendored as pinned git submodules under `test-fixtures/` (security-vetted —
no custom MSBuild tasks, build-time `Exec` commands, T4 templates, or dedicated Roslyn
analyzers/source generators; see `test-fixtures/NOTES.md` for the full vetting record and which
packages/symbols each fixture exercises).

```bash
git submodule update --init --recursive   # one-time, populates test-fixtures/
dotnet test nuget_mcp_core.IntegrationTests
```

Re-run `scripts/vet-fixtures.sh` whenever a fixture's pinned commit is bumped — it automates the
grep-based half of the vetting checklist.

There is no unit test project yet; CI wiring for either test project is a follow-up, not part of
this repo's current automation.

## Design reference

`analyze_symbol_usage_flow.md` contains sequence and flow diagrams of the symbol-analysis path.
