using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NuGet.Packaging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NugetMcp.Core.Services;

public class NuGetPackageAssemblyResolver : IPackageAssemblyResolver
{
    private readonly ILogger<NuGetPackageAssemblyResolver> _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _assemblyCache = new();
    private readonly string _globalPackagesPath;

    public NuGetPackageAssemblyResolver(ILogger<NuGetPackageAssemblyResolver> logger)
    {
        _logger = logger;
        _globalPackagesPath = GetGlobalPackagesPath();
    }

    public async Task<HashSet<string>> ResolvePackageAssembliesAsync(string packageName, string packageVersion, Project project)
    {
        var cacheKey = $"{packageName}:{packageVersion}";
        
        if (_assemblyCache.TryGetValue(cacheKey, out var cachedAssemblies))
        {
            _logger.LogDebug("Retrieved cached assemblies for {PackageName}@{PackageVersion}", packageName, packageVersion);
            return cachedAssemblies;
        }

        _logger.LogDebug("Resolving assemblies for package {PackageName}@{PackageVersion}", packageName, packageVersion);

        try
        {
            // First ensure packages are restored for the solution
            await EnsurePackagesRestoredAsync(project);
            
            var assemblies = await ResolveAssembliesFromLocalPackageAsync(packageName, packageVersion, project);
            
            _assemblyCache.TryAdd(cacheKey, assemblies);
            
            _logger.LogInformation("Resolved {AssemblyCount} assemblies for package {PackageName}@{PackageVersion}: {AssemblyNames}",
                assemblies.Count, packageName, packageVersion, string.Join(", ", assemblies));
            
            return assemblies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve assemblies for package {PackageName}@{PackageVersion}", packageName, packageVersion);
            return new HashSet<string>();
        }
    }

    private string GetGlobalPackagesPath()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "nuget locals global-packages --list",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                
                if (process.ExitCode == 0)
                {
                    // Output format: "global-packages: /path/to/packages"
                    var line = output.Split('\n').FirstOrDefault(l => l.StartsWith("global-packages:"));
                    if (line != null)
                    {
                        var path = line["global-packages:".Length..].Trim();
                        _logger.LogDebug("Found global packages path: {Path}", path);
                        return path;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get global packages path from dotnet CLI");
        }

        // Fallback to default location
        var fallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        _logger.LogDebug("Using fallback global packages path: {Path}", fallbackPath);
        return fallbackPath;
    }

    private async Task EnsurePackagesRestoredAsync(Project project)
    {
        try
        {
            var solutionDir = Path.GetDirectoryName(project.Solution.FilePath);
            if (string.IsNullOrEmpty(solutionDir))
            {
                _logger.LogWarning("Could not determine solution directory for package restore");
                return;
            }

            _logger.LogDebug("Ensuring packages are restored for solution at {SolutionPath}", solutionDir);

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "restore",
                WorkingDirectory = solutionDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    _logger.LogDebug("Package restore completed successfully");
                }
                else
                {
                    _logger.LogWarning("Package restore failed with exit code {ExitCode}. Error: {Error}", process.ExitCode, error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore packages");
        }
    }

    private async Task<HashSet<string>> ResolveAssembliesFromLocalPackageAsync(string packageName, string packageVersion, Project project)
    {
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // First try to get assemblies from Roslyn's metadata references
            var assembliesFromRoslyn = await GetAssembliesFromRoslynAsync(packageName, project);
            if (assembliesFromRoslyn.Count > 0)
            {
                _logger.LogDebug("Found {Count} assemblies from Roslyn metadata for package {PackageName}", assembliesFromRoslyn.Count, packageName);
                return assembliesFromRoslyn;
            }

            // Fallback to scanning the global packages folder
            var assembliesFromDisk = await GetAssembliesFromDiskAsync(packageName, packageVersion);
            if (assembliesFromDisk.Count > 0)
            {
                _logger.LogDebug("Found {Count} assemblies from disk for package {PackageName}@{PackageVersion}", assembliesFromDisk.Count, packageName, packageVersion);
                return assembliesFromDisk;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve assemblies for package {PackageName}@{PackageVersion}, falling back to simple name matching", packageName, packageVersion);
        }

        // Final fallback - use package name as assembly name
        assemblies.Add(packageName);
        _logger.LogWarning("No assemblies found for package {PackageName}@{PackageVersion}, using package name as fallback", packageName, packageVersion);

        return assemblies;
    }

    private async Task<HashSet<string>> GetAssembliesFromRoslynAsync(string packageName, Project project)
    {
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation != null)
            {
                foreach (var reference in compilation.References)
                {
                    if (reference is PortableExecutableReference peRef && !string.IsNullOrEmpty(peRef.FilePath))
                    {
                        var path = peRef.FilePath;
                        
                        // Check if this reference comes from the package we're looking for
                        if (path.Contains(packageName, StringComparison.OrdinalIgnoreCase) && 
                            path.Contains("packages", StringComparison.OrdinalIgnoreCase))
                        {
                            var assemblyName = Path.GetFileNameWithoutExtension(path);
                            assemblies.Add(assemblyName);
                            _logger.LogDebug("Found assembly {AssemblyName} from Roslyn reference for package {PackageName}", assemblyName, packageName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get assemblies from Roslyn for package {PackageName}", packageName);
        }

        return assemblies;
    }

    private async Task<HashSet<string>> GetAssembliesFromDiskAsync(string packageName, string packageVersion)
    {
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var packagePath = Path.Combine(_globalPackagesPath, packageName.ToLowerInvariant(), packageVersion.ToLowerInvariant());
            
            if (!Directory.Exists(packagePath))
            {
                _logger.LogDebug("Package directory not found at {PackagePath}", packagePath);
                return assemblies;
            }

            _logger.LogDebug("Scanning package directory at {PackagePath}", packagePath);

            // Look for .nupkg file to read its contents
            var nupkgFile = Directory.GetFiles(packagePath, "*.nupkg").FirstOrDefault();
            if (nupkgFile != null)
            {
                using var stream = File.OpenRead(nupkgFile);
                using var packageReader = new PackageArchiveReader(stream);
                
                var libItems = await packageReader.GetLibItemsAsync(CancellationToken.None);
                foreach (var libItem in libItems)
                {
                    foreach (var file in libItem.Items)
                    {
                        if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            var assemblyName = Path.GetFileNameWithoutExtension(file);
                            assemblies.Add(assemblyName);
                        }
                    }
                }

                var refItems = await packageReader.GetReferenceItemsAsync(CancellationToken.None);
                foreach (var refItem in refItems)
                {
                    foreach (var file in refItem.Items)
                    {
                        if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            var assemblyName = Path.GetFileNameWithoutExtension(file);
                            assemblies.Add(assemblyName);
                        }
                    }
                }
            }
            else
            {
                // Fallback: scan lib directories for DLL files
                var libDirs = Directory.GetDirectories(packagePath, "lib", SearchOption.TopDirectoryOnly);
                foreach (var libDir in libDirs)
                {
                    var dllFiles = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories);
                    foreach (var dllFile in dllFiles)
                    {
                        var assemblyName = Path.GetFileNameWithoutExtension(dllFile);
                        assemblies.Add(assemblyName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan package directory for {PackageName}@{PackageVersion}", packageName, packageVersion);
        }

        return assemblies;
    }
}