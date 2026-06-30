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
        using var timeoutCts = new CancellationTokenSource();
        var delayTask = Task.Delay(timeout, timeoutCts.Token);
        try
        {
            try
            {
                var winner = await Task.WhenAny(analysisTask, delayTask);
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
                    $"solution={solutionPath}). Per nuget_mcp_core/CLAUDE.md, AnalyzeSymbolAsync rethrows " +
                    "top-level failures (a genuine analyzer bug is one possibility), while a timeout or an " +
                    "unexpected exception from AnalyzeAsync more often points at a submodule checkout or build " +
                    "problem instead -- verify 'git submodule update --init --recursive' was run and the pinned " +
                    "commit still builds, then check the inner exception for details either way.",
                    ex);
            }
        }
        finally
        {
            // Cancel and observe the delay task on every path (success, timeout, or analysis
            // failure) so its TaskCanceledException never goes unobserved.
            timeoutCts.Cancel();
            try
            {
                await delayTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
