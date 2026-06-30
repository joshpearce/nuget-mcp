# nuget_mcp_core — Analysis Engine

Last verified: 2026-06-30

## Purpose
The shared analysis engine behind all three frontends. Given a solution and a target (a NuGet
package or a single symbol), it uses Roslyn semantic analysis to find every real usage and
returns a structured `AnalysisResult`. Exists so the stdio server, HTTP server, and CLI share
one implementation and one set of contracts.

## Contracts
- **Exposes**: `IPackageUsageAnalyzer` with `AnalyzeAsync(solutionPath, packageName, packageVersion, forceRefresh, contextLines, usageTypeFilters?)` and `AnalyzeSymbolAsync(solutionPath, targetSymbol, forceRefresh, contextLines, usageTypeFilters?)`. Both return `AnalysisResult` (usages + analyzed projects + errors + duration).
- **Exposes**: `Extensions/ServiceCollectionExtensions.AddNugetMcpCore(IServiceCollection, IConfiguration)` — registers the full service graph (the 7 singletons + `CodeSimilarity`/`UsageTypeFilter` config binding) in one call. All three frontends and `nuget_mcp_core.IntegrationTests` call this instead of duplicating the registration list; it expects logging already registered on the `IServiceCollection` (`ILogger<T>` for every service) — `Host.CreateApplicationBuilder`/`WebApplication.CreateBuilder` provide this for free, a bare `ServiceCollection` needs an explicit `.AddLogging()` first.
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
- **Possible MSBuild node-reuse deadlock across repeated in-process calls**: `nuget_mcp_core.IntegrationTests` reliably reproduced a 5+ minute hang when one process ran a symbol analysis (in-process `MSBuildWorkspace` via `RoslynSolutionLoader`) followed by a package analysis (shells out to `dotnet restore` via `NuGetPackageAssemblyResolver.EnsurePackagesRestoredAsync`) against the same solution — MSBuild's node-reuse IPC between the in-process engine and the child `dotnet restore` process appears to deadlock. The test fixture works around this by setting `MSBUILDDISABLENODEREUSE=1` before any analysis runs (see `AnalyzerFixture`'s static constructor) — **this mitigation is test-only and has not been applied to production code**. Any long-lived process that calls `AnalyzeAsync`/`AnalyzeSymbolAsync` more than once — the CLI's `batch` command, or either MCP server across multiple tool calls — may be exposed to the same deadlock risk. Investigating/fixing this in `nuget_mcp_core` itself is a suggested follow-up, not yet done.
- **Concurrent `dotnet restore` race on a never-before-restored solution**: `PackageUsageAnalyzer.AnalyzeAsync` scans every project in a solution in parallel (`TaskParallelExecutor`), and each project independently calls `NuGetPackageAssemblyResolver.EnsurePackagesRestoredAsync`, which shells out to `dotnet restore` for the *whole solution* — with no de-duplication or locking across those concurrent calls. On a solution with no prior restore output (e.g. a freshly checked-out repo, before any `obj/*.nuget.*` files exist), this becomes N simultaneous `dotnet restore` invocations racing on the same `obj/` output, all failing — reproduced reliably (10/10 concurrent restores exiting code 1 against a freshly re-checked-out `eShopOnWeb.sln`), silently producing 0 usages instead of the real count (no `Errors` entry — restore failures are only logged as warnings, see `EnsurePackagesRestoredAsync`). `nuget_mcp_core.IntegrationTests`' `AnalyzerFixture` works around this with a one-time sequential `dotnet restore` per fixture solution in `InitializeAsync` before any test runs — **this mitigation is test-only**; any first-time `AnalyzeAsync` call against an unrestored multi-project solution (a likely real scenario: a user's first analysis of a freshly cloned target solution) can hit this same race in production. De-duplicating/serializing restore-per-solution-path in `NuGetPackageAssemblyResolver` itself is a suggested follow-up, not yet done.
- **Similarity de-dup is O(n²)** over kept usages and runs only when `CodeSimilarity.Enabled`. It collapses near-identical snippets, so reported counts can be lower than raw usages; disable it to see everything.
- **Usage-type filtering** runs last: an explicit `usageTypeFilters` argument overrides `UsageTypeFilter.IncludedUsageTypes` from config; if neither is set, all `UsageType` values pass.

## Key Files
- `Extensions/ServiceCollectionExtensions.cs` — `AddNugetMcpCore`, the single shared DI registration entry point
- `Services/PackageUsageAnalyzer.cs` — orchestrates load → resolve → scan → similarity → type-filter → cache
- `Services/RoslynSolutionLoader.cs` — MSBuildWorkspace loading + MSBuildLocator registration
- `Services/RoslynUsageScanner.cs` — semantic walk producing `PackageUsageInstance`s
- `Services/NuGetPackageAssemblyResolver.cs` — package → assembly-name resolution (+ `dotnet` shell-outs)
- `Services/CodeSimilarityService.cs`, `Services/Simplifiers/`, `Services/Comparisons/` — snippet de-dup pipeline
- `Services/FileCacheManager.cs` — JSON file cache under temp dir, SHA256 keys
- `Tools/` — the two `[McpServerToolType]` tool definitions
- `Models/` — `AnalysisResult`, `PackageUsageInstance`, `UsageType`, config DTOs
