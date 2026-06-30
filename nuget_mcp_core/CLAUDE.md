# nuget_mcp_core — Analysis Engine

Last verified: 2026-06-30

## Purpose
The shared analysis engine behind all three frontends. Given a solution and a target (a NuGet
package or a single symbol), it uses Roslyn semantic analysis to find every real usage and
returns a structured `AnalysisResult`. Exists so the stdio server, HTTP server, and CLI share
one implementation and one set of contracts.

## Contracts
- **Exposes**: `IPackageUsageAnalyzer` with `AnalyzeAsync(solutionPath, packageName, packageVersion, forceRefresh, contextLines, usageTypeFilters?)` and `AnalyzeSymbolAsync(solutionPath, targetSymbol, forceRefresh, contextLines, usageTypeFilters?)`. Both return `AnalysisResult` (usages + analyzed projects + errors + duration).
- **MCP tools** (`Tools/`): `AnalyzePackageUsageTool.AnalyzePackageUsage` and `AnalyzeSymbolUsageTool.AnalyzeSymbolUsage`, both `[McpServerTool]` static methods returning a JSON string. Discovered via `WithToolsFromAssembly(typeof(AnalyzePackageUsageTool).Assembly)`.
- **Expects**: callers register the full service graph as singletons and call each tool's static `Initialize(analyzer)` once at startup before any tool invocation.

## Key Decisions
- **Static tool initialization**: tool classes are `static` with a static `_analyzer` field set via `Initialize(...)`. The MCP SDK invokes the static `[McpServerTool]` methods; they have no DI scope, so the analyzer is injected through this static back door. Invoking a tool before `Initialize` throws `InvalidOperationException`.
- **Roslyn over text search**: usages come from semantic models, so results reflect true symbol binding, not string matches.
- **Pluggable similarity/simplifiers**: `CodeSimilarityService` builds its simplifier and comparison pipelines from config `Type` strings via switch factories — adding a new simplifier/comparison means adding a case there *and* a config entry.

## Invariants
- `AnalysisResult.TotalUsageCount` is derived (`Usages.Count`); never set it directly.
- Cache keys incorporate the **.sln file's last-write time** — and only that file. Editing project files or sources without touching the `.sln` will **not** invalidate the cache. Use `forceRefresh`/`--force-refresh` when in doubt.
- Symbol analysis reuses the package-analysis result shape: it stores `PackageName = "symbol:<target>"` and `PackageVersion = "N/A"`, and uses a distinct cache namespace (`GenerateCacheKey(sln, target, "symbol", ctx)`).

## Gotchas
- **Asymmetric error handling**: `AnalyzeAsync` catches top-level failures and returns an `AnalysisResult` with `Errors` populated (never throws). `AnalyzeSymbolAsync` **rethrows** top-level failures. Per-project failures are captured as `Errors` entries in both. The MCP tool wrappers catch everything and return a JSON error object regardless.
- **Shells out to `dotnet`**: `NuGetPackageAssemblyResolver` runs `dotnet restore` and `dotnet nuget locals global-packages --list` as subprocesses. The .NET SDK must be on PATH or assembly resolution degrades.
- **Assembly resolution fallback chain**: Roslyn metadata references → scan the `.nupkg`/`lib` dir in the global packages folder → finally fall back to using the package name as the assembly name. The last fallback can over/under-match; check logs when usage counts look wrong.
- **`MSBuildWorkspace` lifecycle**: `RoslynSolutionLoader` registers `MSBuildLocator` once (static guard) and creates+disposes a fresh workspace per `LoadSolutionAsync` call (workspaces are not pooled). `WorkspaceFailed` events are logged as warnings, not thrown.
- **Similarity de-dup is O(n²)** over kept usages and runs only when `CodeSimilarity.Enabled`. It collapses near-identical snippets, so reported counts can be lower than raw usages; disable it to see everything.
- **Usage-type filtering** runs last: an explicit `usageTypeFilters` argument overrides `UsageTypeFilter.IncludedUsageTypes` from config; if neither is set, all `UsageType` values pass.

## Key Files
- `Services/PackageUsageAnalyzer.cs` — orchestrates load → resolve → scan → similarity → type-filter → cache
- `Services/RoslynSolutionLoader.cs` — MSBuildWorkspace loading + MSBuildLocator registration
- `Services/RoslynUsageScanner.cs` — semantic walk producing `PackageUsageInstance`s
- `Services/NuGetPackageAssemblyResolver.cs` — package → assembly-name resolution (+ `dotnet` shell-outs)
- `Services/CodeSimilarityService.cs`, `Services/Simplifiers/`, `Services/Comparisons/` — snippet de-dup pipeline
- `Services/FileCacheManager.cs` — JSON file cache under temp dir, SHA256 keys
- `Tools/` — the two `[McpServerToolType]` tool definitions
- `Models/` — `AnalysisResult`, `PackageUsageInstance`, `UsageType`, config DTOs
