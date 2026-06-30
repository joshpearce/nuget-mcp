using NugetMcp.Core.Models;

namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Resolves paths to the vendored fixture solutions relative to the repo root, regardless of the
/// working directory `dotnet test` happens to be invoked from. Walks up from the test assembly's
/// own output directory (<see cref="AppContext.BaseDirectory"/>) looking for the repo-root marker
/// file (<c>nuget_mcp.sln</c>) rather than assuming a fixed relative offset, since the number of
/// `bin/<Configuration>/<TFM>` segments under the output directory is an implementation detail of
/// the SDK/test host, not something we should hard-code.
/// </summary>
internal static class FixtureRepoPaths
{
    private static readonly Lazy<string> RepoRoot = new(LocateRepoRoot);

    public static string RestSharpSolution => Path.Combine(RepoRoot.Value, "test-fixtures", "restsharp", "RestSharp.sln");

    public static string EShopOnWebSolution => Path.Combine(RepoRoot.Value, "test-fixtures", "eshoponweb", "eShopOnWeb.sln");

    private static string LocateRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "nuget_mcp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root (a directory containing 'nuget_mcp.sln') by walking up " +
            $"from the test assembly's output directory '{AppContext.BaseDirectory}'.");
    }
}

/// <summary>
/// Integration tests that run the real analysis engine against the vendored RestSharp fixture
/// (pinned commit documented below and in <c>test-fixtures/NOTES.md</c>). All calls use
/// <c>forceRefresh: true</c> to sidestep the shared on-disk cache entirely rather than adding
/// cache-isolation infrastructure (see plans/issue-3-integration-tests.md, Step 4).
/// </summary>
[Collection("AnalyzerCollection")]
public class RestSharpFixtureTests
{
    private const string FixtureName = "RestSharp";
    private const string PinnedSha = "cbcc74b8645966cfefbbebd57d54f558ff78c9aa";
    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(5);

    private static readonly string SolutionPath = FixtureRepoPaths.RestSharpSolution;

    private readonly AnalyzerFixture _fixture;

    public RestSharpFixtureTests(AnalyzerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AnalyzeAsync_NewtonsoftJson_FindsJsonSerializerUsages()
    {
        var result = await RunAnalysisAsync(
            _fixture.Analyzer.AnalyzeAsync(SolutionPath, "Newtonsoft.Json", "13.0.1", forceRefresh: true),
            "AnalyzeAsync(Newtonsoft.Json 13.0.1)");

        Assert.True(result.Errors.Count == 0,
            $"Expected no analysis errors, got: [{string.Join("; ", result.Errors)}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");

        var anyJsonSerializerUsage = result.Usages.Any(u => u.SymbolName.Contains("JsonSerializer", StringComparison.Ordinal));
        Assert.True(anyJsonSerializerUsage,
            $"Expected at least one usage with a SymbolName containing 'JsonSerializer'. " +
            $"fixture={FixtureName} pinned={PinnedSha} totalUsages={result.TotalUsageCount}");

        // test-fixtures/NOTES.md documents ~7 raw `grep -rno JsonSerializer` hits across
        // JsonNetSerializer.cs/RestClientExtensions.cs/NewtonsoftJsonTests.cs. The analyzer's
        // count is not guaranteed to match a raw grep 1:1 -- it binds symbols semantically rather
        // than matching text, so it resolves every real reference (including ones spread across
        // multi-line expressions or implicit `var`-typed usages grep's single-line text match
        // wouldn't separately attribute) rather than counting textual occurrences. Empirically
        // observed 18 against the pinned commit; 10-30 keeps headroom around that while still
        // failing if the package stopped being used (low single digits) or something pathological
        // happened (e.g. a resolver bug matching far more broadly than this one type warrants).
        var exactJsonSerializerCount = result.Usages.Count(u => u.SymbolName == "JsonSerializer");
        Assert.True(exactJsonSerializerCount is >= 10 and <= 30,
            $"Expected exact 'JsonSerializer' SymbolName count in range [10, 30], got {exactJsonSerializerCount}. " +
            $"fixture={FixtureName} pinned={PinnedSha}");
    }

    [Fact]
    public async Task AnalyzeSymbolAsync_CsvHelperCsvReader_FindsUsages()
    {
        var result = await RunAnalysisAsync(
            _fixture.Analyzer.AnalyzeSymbolAsync(SolutionPath, "CsvHelper.CsvReader", forceRefresh: true),
            "AnalyzeSymbolAsync(CsvHelper.CsvReader)");

        Assert.True(result.Errors.Count == 0,
            $"Expected no analysis errors, got: [{string.Join("; ", result.Errors)}]. " +
            $"fixture={FixtureName} pinned={PinnedSha}");

        // test-fixtures/NOTES.md documents CsvReader appearing exactly 1 time via a raw
        // `grep -rno` of the identifier in CsvHelperSerializer.cs. The Roslyn-based analyzer
        // resolves more than that single text occurrence -- e.g. it separately attributes the
        // type reference in the `new CsvReader(...)` call site plus other semantically-bound
        // member/type references the local var's inferred type touches, which grep's
        // single-identifier text match doesn't capture. Empirically observed 9 against the
        // pinned commit; 3-15 keeps headroom around that while still failing if CsvReader usage
        // disappeared (0-1) or a resolver bug matched far more broadly than this one type
        // warrants.
        var csvReaderUsageCount = result.Usages.Count;
        Assert.True(csvReaderUsageCount is >= 3 and <= 15,
            $"Expected CsvHelper.CsvReader usage count in range [3, 15], got {csvReaderUsageCount}. " +
            $"fixture={FixtureName} pinned={PinnedSha}");
    }

    /// <summary>
    /// Awaits <paramref name="analysisTask"/> with a generous per-fixture timeout (real
    /// MSBuildWorkspace + Roslyn analysis against a multi-project solution). <see
    /// cref="IPackageUsageAnalyzer"/>'s methods don't accept a <see cref="CancellationToken"/>, so
    /// this can't truly cancel the underlying work on timeout -- it stops waiting for it and fails
    /// fast with a clear, attributable message instead. Any exception (including our own
    /// synthesized timeout) is wrapped so the failure message always names the fixture and its
    /// pinned commit, distinguishing "fixture/build is broken" from "an assertion below failed".
    /// </summary>
    private static async Task<AnalysisResult> RunAnalysisAsync(Task<AnalysisResult> analysisTask, string operationDescription)
    {
        try
        {
            var winner = await Task.WhenAny(analysisTask, Task.Delay(AnalysisTimeout));
            if (winner != analysisTask)
            {
                throw new TimeoutException($"{operationDescription} did not complete within {AnalysisTimeout}.");
            }

            return await analysisTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{operationDescription} failed against fixture={FixtureName} (pinned commit {PinnedSha}, " +
                $"solution={SolutionPath}). This usually indicates a submodule checkout or build problem " +
                "rather than an analyzer logic bug -- verify 'git submodule update --init --recursive' was run " +
                "and the pinned commit still builds. See inner exception for details.",
                ex);
        }
    }
}
