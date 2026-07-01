namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Integration tests that demonstrate the CVE-usage reachability capability against the in-repo
/// synthetic SharpZipLib fixture (<c>test-fixtures/sharpziplib-cve</c>, documented in
/// <c>test-fixtures/NOTES.md</c> §3). The fixture pins the vulnerable <c>SharpZipLib 1.3.1</c> in
/// two projects: <c>SafeConsumer</c> (compression/creation only) and <c>ExposedConsumer</c>
/// (extracts untrusted archives via the zip-slip surface). These tests encode the product claim:
/// "references a vulnerable version, but the dangerous code path is never called" is answerable by
/// semantic symbol analysis. All calls use <c>forceRefresh: true</c> to sidestep the shared on-disk
/// cache (same rationale as the other fixture tests).
/// </summary>
[Collection("AnalyzerCollection")]
public class SharpZipLibCveFixtureTests
{
    private const string FixtureName = "SharpZipLibCve";

    // In-repo synthetic fixture -- no submodule pin. The vulnerable package version *is* the
    // stable anchor these assertions depend on, so it stands in for a pinned commit in failures.
    private const string PinnedSha = "in-repo synthetic (SharpZipLib 1.3.1)";
    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(5);

    private const string SafeProject = "SafeConsumer";
    private const string ExposedProject = "ExposedConsumer";

    private static readonly string SolutionPath = FixtureRepoPaths.SharpZipLibCveSolution;

    private readonly AnalyzerFixture _fixture;

    public SharpZipLibCveFixtureTests(AnalyzerFixture fixture)
    {
        _fixture = fixture;
    }

    // The vulnerable-symbol signature (see plans/cve-usage-detection-phase1-signature.md).
    // Member forms carry the exact parameter-type list because a method's ToDisplayString() (which
    // RoslynUsageScanner.IsSymbolMatch compares against) includes it -- the bare member name matches
    // nothing.
    [Theory]
    [InlineData("ICSharpCode.SharpZipLib.Zip.FastZip.ExtractZip(string, string, string)", "ZIP zip-slip (CVE-2021-32842)")]
    [InlineData("ICSharpCode.SharpZipLib.Tar.TarArchive.ExtractContents(string)", "TAR traversal (CVE-2021-32840/-32841)")]
    [InlineData("ICSharpCode.SharpZipLib.Zip.ZipInputStream", "hand-rolled ZIP extraction")]
    public async Task AnalyzeSymbolAsync_VulnerableSymbol_ReachableOnlyInExposedConsumer(string vulnerableSymbol, string cve)
    {
        var result = await FixtureAnalysisRunner.RunAsync(
            _fixture.Analyzer.AnalyzeSymbolAsync(SolutionPath, vulnerableSymbol, forceRefresh: true),
            $"AnalyzeSymbolAsync({vulnerableSymbol})", FixtureName, PinnedSha, SolutionPath, AnalysisTimeout);

        Assert.True(result.Errors.Count == 0,
            $"Expected no analysis errors, got: [{string.Join("; ", result.Errors)}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");

        var safeUsages = result.Usages.Where(u => u.ProjectName == SafeProject).ToList();
        var exposedUsages = result.Usages.Where(u => u.ProjectName == ExposedProject).ToList();

        // Negative control (the headline claim): the safe consumer references the vulnerable
        // package version but never calls the vulnerable API -> zero usages -> not reachable.
        Assert.True(safeUsages.Count == 0,
            $"Expected 0 usages of vulnerable symbol '{vulnerableSymbol}' ({cve}) in {SafeProject}, " +
            $"got {safeUsages.Count}: [{string.Join("; ", safeUsages.Select(u => $"{u.FilePath}:{u.StartLine}"))}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");

        // Positive control: the exposed consumer calls the vulnerable API -> reachable, flag it.
        Assert.True(exposedUsages.Count >= 1,
            $"Expected >=1 usage of vulnerable symbol '{vulnerableSymbol}' ({cve}) in {ExposedProject}, " +
            $"got {exposedUsages.Count}. fixture={FixtureName} pinned={PinnedSha}");
    }

    [Fact]
    public async Task AnalyzeSymbolAsync_SafeConsumer_StillUsesLibraryElsewhere()
    {
        // Proves the negative result above is "the vulnerable API is not called", not "the library
        // is unused": the safe consumer genuinely uses SharpZipLib -- on the safe compression
        // surface (GZipOutputStream), which lives only in SafeConsumer here.
        const string safeSymbol = "ICSharpCode.SharpZipLib.GZip.GZipOutputStream";

        var result = await FixtureAnalysisRunner.RunAsync(
            _fixture.Analyzer.AnalyzeSymbolAsync(SolutionPath, safeSymbol, forceRefresh: true),
            $"AnalyzeSymbolAsync({safeSymbol})", FixtureName, PinnedSha, SolutionPath, AnalysisTimeout);

        Assert.True(result.Errors.Count == 0,
            $"Expected no analysis errors, got: [{string.Join("; ", result.Errors)}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");

        var safeUsages = result.Usages.Where(u => u.ProjectName == SafeProject).ToList();
        Assert.True(safeUsages.Count >= 1,
            $"Expected {SafeProject} to use the library on the safe surface ('{safeSymbol}'), " +
            $"got {safeUsages.Count} usages. fixture={FixtureName} pinned={PinnedSha}");
    }

    [Fact]
    public async Task AnalyzeSymbolAsync_MemberTargeting_DiscriminatesExtractFromCreate()
    {
        // The strongest form of the claim: on the *dual-use* FastZip type, member-level targeting
        // separates the vulnerable ExtractZip from the benign CreateZip. If symbol analysis could
        // only match at the type level, both would collapse together and the distinction would be
        // lost -- which is exactly the failure mode this capability avoids.
        const string extract = "ICSharpCode.SharpZipLib.Zip.FastZip.ExtractZip(string, string, string)";
        const string create = "ICSharpCode.SharpZipLib.Zip.FastZip.CreateZip(string, string, bool, string)";

        var extractResult = await FixtureAnalysisRunner.RunAsync(
            _fixture.Analyzer.AnalyzeSymbolAsync(SolutionPath, extract, forceRefresh: true),
            $"AnalyzeSymbolAsync({extract})", FixtureName, PinnedSha, SolutionPath, AnalysisTimeout);
        var createResult = await FixtureAnalysisRunner.RunAsync(
            _fixture.Analyzer.AnalyzeSymbolAsync(SolutionPath, create, forceRefresh: true),
            $"AnalyzeSymbolAsync({create})", FixtureName, PinnedSha, SolutionPath, AnalysisTimeout);

        // Each targets a distinct call site (distinct source line), so neither is empty and the two
        // do not overlap on the same location -- proving they are discriminated, not conflated.
        var extractLocations = extractResult.Usages.Select(u => (u.FilePath, u.StartLine)).ToHashSet();
        var createLocations = createResult.Usages.Select(u => (u.FilePath, u.StartLine)).ToHashSet();

        Assert.True(extractLocations.Count >= 1,
            $"Expected ExtractZip to match >=1 location, got {extractLocations.Count}. " +
            $"fixture={FixtureName} pinned={PinnedSha}");
        Assert.True(createLocations.Count >= 1,
            $"Expected CreateZip to match >=1 location, got {createLocations.Count}. " +
            $"fixture={FixtureName} pinned={PinnedSha}");
        Assert.False(extractLocations.Overlaps(createLocations),
            $"Expected ExtractZip and CreateZip to match disjoint locations, but they shared: " +
            $"[{string.Join("; ", extractLocations.Where(createLocations.Contains).Select(l => $"{l.FilePath}:{l.StartLine}"))}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");
    }
}
