namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Integration tests that run the real analysis engine against the vendored eShopOnWeb fixture
/// (pinned commit documented below and in <c>test-fixtures/NOTES.md</c>). All calls use
/// <c>forceRefresh: true</c> to sidestep the shared on-disk cache entirely rather than adding
/// cache-isolation infrastructure (see plans/issue-3-integration-tests.md, Step 4).
/// </summary>
[Collection("AnalyzerCollection")]
public class EShopOnWebFixtureTests
{
    private const string FixtureName = "EShopOnWeb";
    private const string PinnedSha = "d22efcc6f4ed97c1e2583b013c7ac9d5300558f0";
    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(5);

    private static readonly string SolutionPath = FixtureRepoPaths.EShopOnWebSolution;

    private readonly AnalyzerFixture _fixture;

    public EShopOnWebFixtureTests(AnalyzerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AnalyzeAsync_ArdalisGuardClauses_FindsGuardUsages()
    {
        var result = await FixtureAnalysisRunner.RunAsync(
            _fixture.Analyzer.AnalyzeAsync(SolutionPath, "Ardalis.GuardClauses", "4.0.1", forceRefresh: true),
            "AnalyzeAsync(Ardalis.GuardClauses 4.0.1)", FixtureName, PinnedSha, SolutionPath, AnalysisTimeout);

        Assert.True(result.Errors.Count == 0,
            $"Expected no analysis errors, got: [{string.Join("; ", result.Errors)}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");

        var guardRelatedUsages = result.Usages.Where(u => u.SymbolName.Contains("Guard", StringComparison.Ordinal)).ToList();

        // test-fixtures/NOTES.md documents ~25 raw `Guard.Against.*` call sites across 10 files.
        // Each call site (`Guard.Against.NullOrEmpty(...)`) references the static `Guard` class by
        // name exactly once, so the analyzer's count of SymbolName=="Guard" usages should track
        // that raw count reasonably closely -- but not exactly, since grep counts text occurrences
        // (including any in comments/strings) while the analyzer counts semantically-bound symbol
        // references. 10-50 is wide enough to absorb that gap while still catching a regression
        // where Guard usage essentially disappears or something pathological inflates the count.
        Assert.True(guardRelatedUsages.Count is >= 10 and <= 50,
            $"Expected Guard-related usage count in range [10, 50], got {guardRelatedUsages.Count}. " +
            $"fixture={FixtureName} pinned={PinnedSha}");
    }

    [Fact]
    public async Task AnalyzeSymbolAsync_EntityFrameworkCoreDbContext_FindsCatalogContextUsage()
    {
        // Targeting the exact fully-qualified "Microsoft.EntityFrameworkCore.DbContext" type
        // (rather than the whole "Microsoft.EntityFrameworkCore" namespace) on purpose: an
        // earlier draft of this test targeted the bare namespace and observed 3651 matches
        // (every symbol anywhere in that namespace across the whole solution -- ModelBuilder,
        // DbSet<T> construction, query operators, etc.), which is too broad to be a meaningful
        // "coarse fact" assertion. The exact type match is precisely tied to the documented fact
        // in test-fixtures/NOTES.md: `CatalogContext : DbContext` in
        // src/Infrastructure/Data/CatalogContext.cs (the solution's only direct `: DbContext`
        // base-class reference -- AppIdentityDbContext derives from IdentityDbContext<T> instead).
        var result = await FixtureAnalysisRunner.RunAsync(
            _fixture.Analyzer.AnalyzeSymbolAsync(SolutionPath, "Microsoft.EntityFrameworkCore.DbContext", forceRefresh: true),
            "AnalyzeSymbolAsync(Microsoft.EntityFrameworkCore.DbContext)", FixtureName, PinnedSha, SolutionPath, AnalysisTimeout);

        Assert.True(result.Errors.Count == 0,
            $"Expected no analysis errors, got: [{string.Join("; ", result.Errors)}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");

        // Empirically observed 2 usages against the pinned commit (the `: DbContext` base-list
        // reference plus one additional semantically-bound reference the Roslyn-based analyzer
        // resolves that a raw grep wouldn't separate out). 1-5 keeps the assertion meaningful
        // (it would catch CatalogContext's base class changing or EF Core usage disappearing
        // entirely) while tolerating that small, well-understood analyzer-vs-grep gap.
        var usageCount = result.Usages.Count;
        Assert.True(usageCount is >= 1 and <= 5,
            $"Expected Microsoft.EntityFrameworkCore.DbContext usage count in range [1, 5], got {usageCount}. " +
            $"fixture={FixtureName} pinned={PinnedSha}");
    }
}
