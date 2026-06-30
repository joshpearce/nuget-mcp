# Fixture repos for `nuget_mcp_core` integration tests

Vendored as pinned git submodules under `test-fixtures/`. Both are real, third-party OSS .NET
repos used to exercise `IPackageUsageAnalyzer` against actual NuGet package/symbol usage rather
than synthetic test solutions. Pins are exact commit SHAs (not moving branches/tags) so test
assertions stay stable until someone deliberately bumps them.

Re-vet on every pin bump: re-run the checklist in "Vetting checklist" below (and, once it
exists, `scripts/vet-fixtures.sh` from Step 5) against the new commit before updating the
submodule pointer.

## 1. RestSharp (`test-fixtures/restsharp`)

- Repo: https://github.com/restsharp/RestSharp.git
- Pinned commit: `cbcc74b8645966cfefbbebd57d54f558ff78c9aa` (tag `108.0.3`, 2022-11-08)
- Why this commit, not the latest tag (`114.0.0`): the plan's starting candidate was
  `restsharp/RestSharp` as "a plain library with non-trivial NuGet consumption." Every tagged
  release from `109.0.0` through `114.0.0` (the current HEAD-of-tags) fails the security
  checklist: `src/RestSharp/RestSharp.csproj` references `gen/SourceGenerator/SourceGenerator.csproj`
  as `OutputItemType="Analyzer"` — i.e. the main library project builds and runs an in-repo,
  custom Roslyn source generator as part of compiling `RestSharp.dll`. That's exactly the
  "source generator ... PackageReference" rejection criterion in the plan. `114.0.0` additionally
  dropped `RestSharp.sln` in favor of `RestSharp.slnx`, which would break a `.sln`-based fixture
  sanity build anyway.
  `108.0.3` is the last tagged release before the source generator project was introduced
  (added in the `v109 (#1963)` commit). It's a real, tagged, stable release — just not the
  newest — so this is a same-repo "swap" to an earlier commit rather than swapping to a
  different project. No further alternative was needed.
- Solution: `RestSharp.sln` (16 projects: 1 main lib, 4 serializer extension libs, 1
  benchmarks project, 1 DI extension lib, 9 test projects). Targets `netstandard2.0;net5.0;net6.0`
  for `src/*`, `net472;net6.0` for `test/*`.

### Vetting checklist results (RestSharp @ `cbcc74b8645966cfefbbebd57d54f558ff78c9aa`)
Reviewed every `.csproj`, `.sln`, `.targets`, `.props`, `Directory.Build.*` in the tree (`grep -rn`
across the full checkout, then spot-checked each file referenced by `RestSharp.sln`):
- `UsingTask`: none found anywhere in the tree.
- `<Exec Command=...>` / any `Target` running `Exec`: none found. The only custom `<Target>` is
  `CustomVersion` in `src/Directory.Build.props`, which just copies `MinVer*` MSBuild properties
  into `FileVersion`/`AssemblyVersion` — no process execution.
- Source generator / analyzer `PackageReference`s: none in this commit (the only such reference,
  `gen/SourceGenerator`, doesn't exist yet — it's added in `109.0.0`, which is why this commit was
  chosen over later tags). `MinVer` and `Microsoft.SourceLink.GitHub` are present
  (`src/Directory.Build.props`) but these are well-known, widely used build-time-only metadata
  packages (git-tag-derived versioning / source link), not custom or project-authored analyzers.
- NuGet install/restore scripts (`install.ps1`, `init.ps1`, custom restore targets): none found.
- `.tt` (T4) files: none found.
- Verdict: **clean** — no UsingTask/Exec/analyzers/T4 found.

### Known build caveat (does not affect analysis)
A full `dotnet build RestSharp.sln` fails with one error: `test/RestSharp.Tests/RestRequestTests.cs`
doesn't compile under the `net472` TFM (`CS0246: HttpRequestException could not be found`). Root
cause: the .NET SDK only adds an implicit `using System.Net.Http;` for non-`.NETFramework` TFMs,
and this test file relies on that implicit using without an explicit one — it compiles fine under
the project's other TFM (`net6.0`). This is pre-existing upstream test code, confined to one
legacy/Windows-only test TFM; `src/RestSharp` (the code our analyzer actually targets) builds
clean across all of its TFMs (`netstandard2.0`, `net5.0`, `net6.0`). Not a vetting failure and not
a reason to re-pin — noted here so a future re-vet doesn't have to rediscover it.

### Packages/symbols expected to drive Step 4 assertions
- **Newtonsoft.Json** `13.0.1` — referenced by `src/RestSharp.Serializers.NewtonsoftJson/RestSharp.Serializers.NewtonsoftJson.csproj`.
  Non-trivially used in `src/RestSharp.Serializers.NewtonsoftJson/JsonNetSerializer.cs`
  (`JsonSerializer`, `JsonSerializerSettings`, `JsonTextReader`, `CamelCasePropertyNamesContractResolver`)
  and `RestClientExtensions.cs`, plus `test/RestSharp.Tests.Serializers.Json/NewtonsoftJsonTests.cs`.
  `JsonSerializer` symbol alone appears ~7 times across those files (`grep -rno`).
- **CsvHelper** `28.0.1` — referenced by `src/RestSharp.Serializers.CsvHelper/RestSharp.Serializers.CsvHelper.csproj`.
  Non-trivially used in `src/RestSharp.Serializers.CsvHelper/CsvHelperSerializer.cs` (`CsvReader`,
  `CsvWriter`, `CsvConfiguration` — both reader and writer code paths implemented), plus
  `test/RestSharp.Tests.Serializers.Csv/CsvHelperTests.cs`. In `CsvHelperSerializer.cs`,
  `CsvReader` appears 1 time and `CsvWriter` appears 1 time (small but real, both symbols
  exercised, not just referenced — a per-symbol assertion should expect ~1, not ~2).
- Explicitly **not** used as an assertion: `Polly` `7.2.3` is referenced by
  `test/RestSharp.Tests.Integrated/RestSharp.Tests.Integrated.csproj` but at this pinned commit
  no `.cs` file actually uses the `Polly` namespace — referenced-but-unused, so it would make a
  weak/misleading test assertion per the plan's guidance.

## 2. eShopOnWeb (`test-fixtures/eshoponweb`)

- Repo: https://github.com/dotnet-architecture/eShopOnWeb.git
- Pinned commit: `d22efcc6f4ed97c1e2583b013c7ac9d5300558f0` (`main` branch, 2023-09-12, "Merge pull
  request #878 from paisanousa/fix-warning")
- Why this commit, not `main`'s current tip: this repo has no recent tagged releases (its only
  tags — `netcore1.1`, `netcore2.1`, `netcore2.2`, `eShopOnWeb-1.0` — are years-old pre-.NET-Core-3
  snapshots, not representative of the actively maintained app), so `main` is the right line of
  development to pin from. `main`'s current tip (`4da8212117e87d808d4bbc7da6286fd2147ce606`,
  2025-01-13) fails the security checklist: `tests/UnitTests/UnitTests.csproj` and
  `tests/IntegrationTests/IntegrationTests.csproj` both reference the
  `NSubstitute.Analyzers.CSharp` package with `IncludeAssets: ...analyzers;buildtransitive` — a
  dedicated third-party Roslyn analyzer package, added in commit `ee14ef3` ("Updating test
  projects", 2023-09-19). Both `eShopOnWeb.sln` and `Everything.sln` include those test projects,
  so there's no in-repo solution that avoids it once that commit lands. The chosen commit is the
  direct parent of `ee14ef3` — i.e. the last `main` commit before that analyzer was introduced.
  This keeps the fixture on the real, continuously-maintained `main` line (ASP.NET Core MVC +
  Web API + Blazor admin + EF Core, ASP.NET Core Identity, MediatR-based CQRS slices) without the
  custom analyzer. TFM at this commit is `net7.0` (centrally managed via `Directory.Packages.props`),
  not yet `net8.0` — `main` only migrates to `net8.0` later (see `dependabot/nuget/AspNetVersion-8.0.5`
  branch) — but nothing in the plan requires `net8.0` specifically, and `net7.0` builds fine
  under the .NET 8 SDK used by this repo.
- Solution: `eShopOnWeb.sln` (8 leaf projects: `Web`, `Infrastructure`, `ApplicationCore`,
  `PublicApi`, `BlazorAdmin`, `BlazorShared`, plus `UnitTests`, `IntegrationTests`,
  `FunctionalTests`, `PublicApiIntegrationTests`, plus a `docker-compose.dcproj`). An equivalent
  `Everything.sln` also exists at the repo root with the same project set in a different solution
  folder layout; `eShopOnWeb.sln` is the one referenced in the repo's own docs/CI, so that's the
  one tests should point at.

### Vetting checklist results (eShopOnWeb @ `d22efcc6f4ed97c1e2583b013c7ac9d5300558f0`)
Reviewed every `.csproj` (12), both `.sln` files, and `Directory.Packages.props` (the repo has no
`Directory.Build.props`/`.targets` and no other `.props`/`.targets` files); full-tree `grep -rn`
plus a per-file read of every project referenced by `eShopOnWeb.sln`:
- `UsingTask`: none found anywhere in the tree.
- `<Exec Command=...>` / build-event targets: none found in any `.csproj`/`.sln`/`.props`.
- Source generator / analyzer `PackageReference`s: **none of the rejection-worthy kind** at this
  commit. `NSubstitute.Analyzers.CSharp` (the actual trigger for rejecting `main`'s tip) is absent
  here — confirmed via `grep -rn "NSubstitute"` returning nothing. The checklist did surface
  `IncludeAssets: ...analyzers...` metadata on `Microsoft.EntityFrameworkCore.Tools` (`src/Web`,
  `src/PublicApi`), `xunit.runner.visualstudio` / `xunit.runner.console`
  (`tests/UnitTests`, `tests/IntegrationTests`), and `coverlet.collector`
  (`tests/PublicApiIntegrationTests`). These are judged **acceptable, not a vetting failure**:
  they're ubiquitous, well-known Microsoft/community build- and test-infrastructure packages
  (EF Core design-time tooling, VSTest discovery adapter, code-coverage collector) whose NuGet
  packaging happens to flag an `analyzers` asset bucket for IDE/build integration — they do not
  ship custom diagnostic/codegen logic targeting this app's source the way
  `NSubstitute.Analyzers.CSharp` or RestSharp's in-repo `gen/SourceGenerator` do. Distinguishing
  "ecosystem-standard tooling package" from "dedicated analyzer/source-generator" was the
  judgment call applied consistently across both fixtures.
- NuGet install/restore scripts (`install.ps1`, `init.ps1`): none found.
- `.tt` (T4) files: none found.
- Noted but not a rejection: `src/Web/Web.csproj` has
  `<PackageReference Include="BuildBundlerMinifier" Condition="'$(Configuration)'=='Release'" />`.
  This package ships MSBuild `.targets` that hook into the `Build` target to bundle/minify static
  assets — real build-time behavior, but (a) it's gated to `Release` configuration only and our
  fixture builds/analysis will use the default `Debug` configuration, and (b) `MSBuildWorkspace`
  project loading (what `nuget_mcp_core` uses) does a design-time evaluation, not an actual
  `Build` target invocation, so this target won't execute during analysis either way. Also noted:
  `tests/FunctionalTests/FunctionalTests.csproj` has a stale
  `<DotNetCliToolReference Include="dotnet-xunit" ... />` — the old (pre-`dotnet tool`) CLI tool
  reference mechanism, which modern SDKs treat as a no-op rather than a restore script.
- Verdict: **clean** — no UsingTask/Exec/custom analyzers/T4 found; two borderline items
  (ecosystem `analyzers` asset metadata, conditional `BuildBundlerMinifier`) reviewed and judged
  acceptable for the reasons above.

### Packages/symbols expected to drive Step 4 assertions
- **Ardalis.GuardClauses** `4.0.1` — referenced by `src/ApplicationCore/ApplicationCore.csproj`.
  Heavily used: `Guard.Against.*` call sites appear ~25 times (`grep -rno`) across 10 files,
  including `src/ApplicationCore/Services/BasketService.cs`, `OrderService.cs`,
  `src/Web/Controllers/OrderController.cs`, `ManageController.cs`, and the Identity Razor pages.
- **Microsoft.EntityFrameworkCore.SqlServer** `7.0.5` (and `Microsoft.EntityFrameworkCore.InMemory`
  `7.0.5`) — referenced by `src/Infrastructure/Infrastructure.csproj`. Non-trivially used in
  `src/Infrastructure/Data/CatalogContext.cs`: `CatalogContext : DbContext` with 7 `DbSet<T>`
  properties (`Baskets`, `CatalogItems`, `CatalogBrands`, `CatalogTypes`, `Orders`, `OrderItems`,
  `BasketItems`).
- Secondary candidates if more assertions are wanted later: **Ardalis.Specification** `7.0.0`
  (`Specification<T>` used ~11 times across 8 files) and **MediatR** `12.0.1`
  (`IRequestHandler<...>` used in `src/Web/Features/OrderDetails/*`, `MyOrders/*`,
  `OrderController.cs` — smaller surface, ~2 handler implementations).
- Explicitly **not** used as an assertion: `FluentValidation` `11.7.1` is referenced by
  `src/BlazorShared/BlazorShared.csproj` but no `.cs` file in the tree uses
  `FluentValidation`/`AbstractValidator` at this pinned commit — referenced-but-unused.

## Re-vetting on pin bumps

If either submodule's pinned commit is ever updated, re-run the full checklist above (UsingTask,
`<Exec>` build events, analyzer/source-generator `PackageReference`s, NuGet install/init scripts,
`.tt` files) against the new commit, and re-confirm the packages/symbols listed above still match
(upstream changes can rename, remove, or stop using a given symbol). Also run
`scripts/vet-fixtures.sh` — it automates the grep-based half of this checklist (UsingTask, `<Exec>`
build events, `.tt` files, and dedicated analyzer/source-generator packages or project references
matched by name), but the source-generator/analyzer judgment calls documented above (ecosystem
tooling vs. dedicated analyzer) still need a human read on any new finding it flags. The script
deliberately does not flag a package merely for listing `analyzers` in its `IncludeAssets` (e.g.
`Microsoft.EntityFrameworkCore.Tools`, `xunit.runner.visualstudio`, `coverlet.collector`) — only a
package/project whose own name contains `Analyzer`/`SourceGenerator`, or an `OutputItemType="Analyzer"`
reference, the same distinction drawn manually above.
