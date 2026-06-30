namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Smoke test for <see cref="AnalyzerFixture"/> itself: verifies the shared DI graph resolves
/// an <see cref="Services.IPackageUsageAnalyzer"/> without throwing. Fixture-repo tests (RestSharp,
/// eShopOnWeb) are added in a later step.
/// </summary>
[Collection("AnalyzerCollection")]
public class AnalyzerFixtureTests
{
    private readonly AnalyzerFixture _fixture;

    public AnalyzerFixtureTests(AnalyzerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Analyzer_IsResolved()
    {
        Assert.NotNull(_fixture.Analyzer);
    }
}
