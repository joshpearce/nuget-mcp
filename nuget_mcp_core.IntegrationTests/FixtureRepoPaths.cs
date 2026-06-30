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
