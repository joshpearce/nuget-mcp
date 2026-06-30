using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace NugetMcp.Core.Services;

public class RoslynSolutionLoader : ISolutionLoader
{
    private readonly ILogger<RoslynSolutionLoader> _logger;
    private static bool _msBuildLocatorRegistered = false;
    private static readonly object _lockObject = new();

    public RoslynSolutionLoader(ILogger<RoslynSolutionLoader> logger)
    {
        _logger = logger;
        EnsureMSBuildLocatorRegistered();
    }

    public async Task<Solution> LoadSolutionAsync(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");
        }

        _logger.LogInformation("Loading solution from {SolutionPath}", solutionPath);

        try
        {
            using var workspace = MSBuildWorkspace.Create();
            workspace.WorkspaceFailed += (sender, e) =>
            {
                _logger.LogWarning("Workspace warning: {Diagnostic}", e.Diagnostic.Message);
            };

            var solution = await workspace.OpenSolutionAsync(solutionPath);
            
            _logger.LogInformation("Loaded solution with {ProjectCount} projects: {ProjectNames}",
                solution.Projects.Count(),
                string.Join(", ", solution.Projects.Select(p => p.Name)));

            foreach (var project in solution.Projects)
            {
                _logger.LogDebug("Project: {ProjectName} ({ProjectFilePath}) - {DocumentCount} documents",
                    project.Name, project.FilePath, project.Documents.Count());
            }

            return solution;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load solution from {SolutionPath}", solutionPath);
            throw;
        }
    }

    private static void EnsureMSBuildLocatorRegistered()
    {
        lock (_lockObject)
        {
            if (!_msBuildLocatorRegistered)
            {
                if (!MSBuildLocator.IsRegistered)
                {
                    MSBuildLocator.RegisterDefaults();
                }
                _msBuildLocatorRegistered = true;
            }
        }
    }
}