using NugetMcp.Core.Models;

namespace NugetMcp.Core.IntegrationTests;

/// <summary>
/// Shared timeout/error-wrapping logic for fixture integration tests. Awaits an analysis call with
/// a generous per-fixture timeout (real MSBuildWorkspace + Roslyn analysis against a multi-project
/// solution). <see cref="Services.IPackageUsageAnalyzer"/>'s methods don't accept a <see
/// cref="CancellationToken"/>, so this can't truly cancel the underlying work on timeout -- it stops
/// waiting for it and fails fast with a clear, attributable message instead. Any exception
/// (including the synthesized timeout) is wrapped so the failure message always names the fixture
/// and its pinned commit, distinguishing "fixture/build is broken" from "an assertion below failed".
/// </summary>
internal static class FixtureAnalysisRunner
{
    public static async Task<AnalysisResult> RunAsync(
        Task<AnalysisResult> analysisTask,
        string operationDescription,
        string fixtureName,
        string pinnedSha,
        string solutionPath,
        TimeSpan timeout)
    {
        try
        {
            var winner = await Task.WhenAny(analysisTask, Task.Delay(timeout));
            if (winner != analysisTask)
            {
                throw new TimeoutException($"{operationDescription} did not complete within {timeout}.");
            }

            return await analysisTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"{operationDescription} failed against fixture={fixtureName} (pinned commit {pinnedSha}, " +
                $"solution={solutionPath}). This usually indicates a submodule checkout or build problem " +
                "rather than an analyzer logic bug -- verify 'git submodule update --init --recursive' was run " +
                "and the pinned commit still builds. See inner exception for details.",
                ex);
        }
    }
}
