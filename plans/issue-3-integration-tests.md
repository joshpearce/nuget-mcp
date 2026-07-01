# Plan: Real-world integration tests for `nuget_mcp_core` (issue #3)

## Summary
Add a new `nuget_mcp_core.IntegrationTests` project that runs the existing analysis engine
against two small, vetted, real OSS .NET repos vendored as pinned git submodules, asserting
coarse-but-meaningful facts about package/symbol usage. Along the way, extract the
already-duplicated DI registration block into a shared helper so the test project becomes a
4th consumer of one source of truth instead of a 4th hand-copy. Security posture for v1 is
manual vetting + an automated grep-based guard script, not full sandboxing — that's deferred.
CI wiring is also deferred to a follow-up issue; this plan only needs tests to exist and pass
locally via `dotnet test`.

## Decisions locked in
- **Fixtures**: start with `restsharp/RestSharp` (plain library, non-trivial NuGet
  consumption) and `dotnet-architecture/eShopOnWeb` (Microsoft reference app: ASP.NET Core MVC
  + EF Core, single solution, no exotic build tooling). Final selection/pin happens during
  Step 1's vetting pass — if either fails the security checklist or lacks confirmed non-trivial
  package/symbol usage, swap in an alternative meeting the same criteria (small, single-`.sln`,
  plain library/app code, no custom MSBuild tasks).
- **Test framework**: xUnit (no existing convention in the repo; xUnit's `ICollectionFixture`
  fits the "build the DI graph once, run sequentially" need).
- **Sandboxing**: manual review only for v1 (per the issue's checklist) — no container/restricted
  execution environment in this issue. Note this explicitly as an accepted, documented risk.
- **CI**: out of scope — `dotnet test` must work locally; wiring a workflow is a follow-up.

## Step 1 — Vendor and vet 2 fixture repos
Add `test-fixtures/restsharp` and `test-fixtures/eshoponweb` as git submodules pinned to a
specific commit SHA each. Before pinning, manually review every `.csproj`/`.sln`/`.targets`/
`.props`/`Directory.Build.*` in each repo for `UsingTask`, `Exec Command` build events, source
generator/analyzer references, NuGet install scripts, and `.tt` files — reject and swap fixtures
that have any. While reviewing, confirm and write down (in a short `test-fixtures/NOTES.md`)
which NuGet packages and symbols each fixture exercises non-trivially, since these facts become
the test assertions in Step 4.

- **Files**: new `.gitmodules`, `test-fixtures/restsharp` (submodule), `test-fixtures/eshoponweb`
  (submodule), `test-fixtures/NOTES.md`.
- **Verify**: `git submodule update --init --recursive` from a clean clone populates both
  fixture directories with the pinned commits; `dotnet build` succeeds standalone against each
  fixture's `.sln` outside of our analyzer (sanity check the fixture itself is buildable).

## Step 2 — Extract shared DI registration
`nuget_mcp_stdio/Program.cs`, `nuget_mcp_http/Program.cs`, and `nuget_usage_analyzer_cli`'s
`CreateHost` currently duplicate the same 8-service singleton block + config binding verbatim.
Extract it into a single `public static IServiceCollection AddNugetMcpCore(this
IServiceCollection services, IConfiguration configuration)` extension method in
`nuget_mcp_core` (e.g. `Extensions/ServiceCollectionExtensions.cs`), and update all three
existing entry points to call it. Behavior-preserving refactor only — no service or config
section changes.

- **Files**: new `nuget_mcp_core/Extensions/ServiceCollectionExtensions.cs`; edits to
  `nuget_mcp_stdio/Program.cs`, `nuget_mcp_http/Program.cs`,
  `nuget_usage_analyzer_cli/Program.cs` (or wherever `CreateHost` lives).
- **Verify**: all three existing projects still build and run unchanged (`dotnet build
  nuget_mcp.sln`); manually smoke-test one frontend (e.g. CLI `analyze` against any local
  solution) to confirm the DI graph still resolves.

## Step 3 — Add the integration test project
Create `nuget_mcp_core.IntegrationTests` (net8.0, xUnit, nullable/implicit-usings enabled to
match conventions), add it to `nuget_mcp.sln`, reference `nuget_mcp_core`. Add a collection
fixture that calls `AddNugetMcpCore` against a minimal `IConfiguration` (no `appsettings.json`
needed — `CodeSimilarity`/`UsageTypeFilter` bind to safe defaults when absent) and resolves
`IPackageUsageAnalyzer`. Mark all integration tests as a single xUnit collection (no
`[CollectionDefinition(DisableParallelization = true)]` needed by default, but be explicit) so
fixture builds never run concurrently against the shared NuGet/analysis caches.

- **Files**: new `nuget_mcp_core.IntegrationTests/nuget_mcp_core.IntegrationTests.csproj`,
  `AnalyzerFixture.cs`; edit `nuget_mcp.sln`.
- **Verify**: `dotnet test` discovers the project and runs zero tests successfully (project
  scaffolding compiles and the fixture constructs an analyzer without throwing).

## Step 4 — Write the integration tests
For each fixture, write tests that call `AnalyzeAsync`/`AnalyzeSymbolAsync` with
`forceRefresh: true` (always — sidesteps the global temp-dir cache entirely rather than adding
cache-isolation infrastructure) and a per-fixture timeout. Assert coarse facts from
`test-fixtures/NOTES.md`: a known package appears in the usage results, a known symbol appears
within an expected range (not an exact count — upstream fixture updates will shift exact
numbers), `Errors` is empty, and the call completes within the timeout. On any submodule/build
failure, the test failure message should include the fixture name and pinned commit SHA so a
pin-rot or checkout problem is obviously distinguishable from an analyzer bug.

- **Files**: `nuget_mcp_core.IntegrationTests/RestSharpFixtureTests.cs`,
  `EShopOnWebFixtureTests.cs`.
- **Verify**: `dotnet test` passes locally from a fresh clone after
  `git submodule update --init --recursive`; deliberately breaking one assertion (e.g. wrong
  package name) makes the corresponding test fail with a clear message.

## Step 5 — Automated dangerous-pattern guard script
Add a small script (e.g. `scripts/vet-fixtures.sh` or a `dotnet run` console snippet) that greps
`test-fixtures/**/*.csproj|*.targets|*.props|*.tt` for `UsingTask`, `Exec Command`, T4
transforms, and analyzer/source-generator `PackageReference`s, exiting non-zero if any are
found. Document in `test-fixtures/NOTES.md` that this must be re-run on every pinned-commit
bump (not just at initial vendoring).

- **Files**: new `scripts/vet-fixtures.sh`; edit `test-fixtures/NOTES.md`.
- **Verify**: running the script against the current pinned fixtures exits 0; running it
  against a fixture with a synthetic `<Target BeforeTargets="Build"><Exec .../></Target>`
  injected locally (not committed) exits non-zero and names the offending file.

## Definition of Done
- All five steps' acceptance criteria pass.
- `dotnet build nuget_mcp.sln` succeeds for all five projects (four original + new test
  project).
- `dotnet test` passes from a fresh clone after submodule init, with no flakiness across two
  consecutive local runs.
- `scripts/vet-fixtures.sh` passes against both pinned fixtures.
- No changes to existing frontend behavior (Step 2 refactor is behavior-preserving — verified
  by manual smoke test).
- CI wiring and containerized sandboxing are explicitly **not** part of this issue; note both as
  follow-ups when closing #3.
