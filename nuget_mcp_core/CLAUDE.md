# nuget_mcp_core — Analysis Engine

Last verified: 2026-07-01

## Purpose
The shared analysis engine behind all three frontends. Given a solution and a target (a NuGet
package or a single symbol), it uses Roslyn semantic analysis to find every real usage and
returns a structured `AnalysisResult`. Exists so the stdio server, HTTP server, and CLI share
one implementation and one set of contracts.

`AnalyzeSymbolAsync` doubles as a **CVE-usage reachability** check: point it at a CVE's vulnerable
symbol(s) to decide whether a solution actually calls the vulnerable API vs merely referencing a
vulnerable version. Capability, the reusable "signature" data shape, and the necessary-not-sufficient
limits are written up in `plans/cve-reachability-capability.md`; a worked SharpZipLib example (with
the exact symbol-string matching rules) is in `plans/cve-usage-detection-phase1-signature.md`, and
the demonstrating fixture + tests are `test-fixtures/sharpziplib-cve` (NOTES.md §3) and
`nuget_mcp_core.IntegrationTests/SharpZipLibCveFixtureTests.cs`.

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
- **Error-handling contract**: both `AnalyzeAsync` and `AnalyzeSymbolAsync` **rethrow** top-level failures (bad path, solution won't load, MSBuild failure) — a catastrophic failure is not a result. Per-project failures are captured as `Errors` entries on the returned `AnalysisResult` in both. So `Errors` means "some projects failed within an otherwise-successful run"; an exception means "the analysis couldn't run at all". The MCP tool wrappers catch everything and return a JSON error object regardless; all three CLI entry points catch and set `ExitCode = 1`.
- **Shells out to `dotnet`**: `NuGetPackageAssemblyResolver` runs `dotnet restore` and `dotnet nuget locals global-packages --list` as subprocesses. The .NET SDK must be on PATH or assembly resolution degrades.
- **Assembly resolution fallback chain**: Roslyn metadata references → scan the `.nupkg`/`lib` dir in the global packages folder → finally fall back to using the package name as the assembly name. The last fallback can over/under-match; check logs when usage counts look wrong.
- **`MSBuildWorkspace` lifecycle**: `RoslynSolutionLoader` registers `MSBuildLocator` once (static guard) and creates+disposes a fresh workspace per `LoadSolutionAsync` call (workspaces are not pooled). `WorkspaceFailed` events are logged as warnings, not thrown.
- **Possible MSBuild node-reuse deadlock across repeated in-process calls**: `nuget_mcp_core.IntegrationTests` reliably reproduced a 5+ minute hang when one process ran a symbol analysis (in-process `MSBuildWorkspace` via `RoslynSolutionLoader`) followed by a package analysis (shells out to `dotnet restore` via `NuGetPackageAssemblyResolver.EnsurePackagesRestoredAsync`) against the same solution — MSBuild's node-reuse IPC between the in-process engine and the child `dotnet restore` process appears to deadlock. The test fixture works around this by setting `MSBUILDDISABLENODEREUSE=1` before any analysis runs (see `AnalyzerFixture`'s static constructor) — **this mitigation is test-only and has not been applied to production code**. Any long-lived process that calls `AnalyzeAsync`/`AnalyzeSymbolAsync` more than once — the CLI's `batch` command, or either MCP server across multiple tool calls — may be exposed to the same deadlock risk. Investigating/fixing this in `nuget_mcp_core` itself is a suggested follow-up, not yet done.
- **Restore is de-duplicated per solution directory (and disables build servers)**: `PackageUsageAnalyzer.AnalyzeAsync` scans every project in a solution in parallel (`TaskParallelExecutor`), and each project independently calls `NuGetPackageAssemblyResolver.EnsurePackagesRestoredAsync`, which shells out to `dotnet restore` for the *whole solution*. To avoid N simultaneous identical full-solution restores, `EnsurePackagesRestoredAsync` now dispatches through a `ConcurrentDictionary<string, Lazy<Task>>` keyed by solution directory, so the underlying `RestoreSolutionAsync` runs **at most once per solution dir for the (singleton) resolver's lifetime**. This fixes both the old concurrent-restore race (N restores racing on the same `obj/` output on a never-before-restored solution — previously silently produced 0 usages, no `Errors` entry since restore failures are only logged as warnings) and the lingering-MSBuild-worker-node accumulation (issue #4). The restore also passes `--disable-build-servers` (`/nodeReuse:false` + no shared compilation) so any nodes it spawns exit when restore finishes instead of lingering ~15 min for reuse; the process-wide `MSBUILDDISABLENODEREUSE` env var alone did not reach these child nodes. Caveat: the dedup Task caches success/failure for the resolver's lifetime, so a solution is restored once per process — a package added to the target solution mid-process won't be re-restored (consistent with `_assemblyCache`, which is also process-wide and not cleared by `forceRefresh`). `AnalyzerFixture` still pre-restores each fixture solution once in `InitializeAsync` as belt-and-suspenders.
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
