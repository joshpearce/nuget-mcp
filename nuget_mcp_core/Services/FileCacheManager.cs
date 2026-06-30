using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using NugetMcp.Core.Models;

namespace NugetMcp.Core.Services;

public class FileCacheManager : ICacheManager
{
    private readonly string _cacheDirectory;
    private readonly ILogger<FileCacheManager> _logger;
    private readonly SemaphoreSlim _cacheSemaphore = new(1, 1);

    public FileCacheManager(ILogger<FileCacheManager> logger)
    {
        _logger = logger;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "nuget-usage-analysis-cache");
        
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
            _logger.LogDebug("Created cache directory: {CacheDirectory}", _cacheDirectory);
        }
    }

    public string GenerateCacheKey(string solutionPath, string packageName, string packageVersion)
    {
        return GenerateCacheKey(solutionPath, packageName, packageVersion, 0);
    }

    public string GenerateCacheKey(string solutionPath, string packageName, string packageVersion, int contextLines)
    {
        var lastModified = GetSolutionLastModified(solutionPath);
        var keyContent = $"{solutionPath}|{packageName}|{packageVersion}|{lastModified:O}|context:{contextLines}";
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyContent));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        
        _logger.LogDebug("Generated cache key {CacheKey} for solution {SolutionPath}, package {PackageName}@{PackageVersion}, contextLines {ContextLines}",
            hash, solutionPath, packageName, packageVersion, contextLines);
        
        return hash;
    }

    public async Task<AnalysisResult?> LoadResultsAsync(string cacheKey)
    {
        await _cacheSemaphore.WaitAsync();
        try
        {
            var cacheFilePath = GetCacheFilePath(cacheKey);
            
            if (!File.Exists(cacheFilePath))
            {
                _logger.LogDebug("Cache miss for key {CacheKey}", cacheKey);
                return null;
            }

            _logger.LogDebug("Loading cached results for key {CacheKey}", cacheKey);
            var jsonContent = await File.ReadAllTextAsync(cacheFilePath);
            var result = JsonSerializer.Deserialize<AnalysisResult>(jsonContent, new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            });
            
            _logger.LogInformation("Loaded cached analysis results for {PackageName}@{PackageVersion} with {UsageCount} usages",
                result?.PackageName, result?.PackageVersion, result?.TotalUsageCount);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load cached results for key {CacheKey}", cacheKey);
            return null;
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }

    public async Task SaveResultsAsync(string cacheKey, AnalysisResult results)
    {
        await _cacheSemaphore.WaitAsync();
        try
        {
            var cacheFilePath = GetCacheFilePath(cacheKey);
            var jsonContent = JsonSerializer.Serialize(results, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });
            
            await File.WriteAllTextAsync(cacheFilePath, jsonContent);
            
            _logger.LogInformation("Cached analysis results for {PackageName}@{PackageVersion} with {UsageCount} usages to {CacheFile}",
                results.PackageName, results.PackageVersion, results.TotalUsageCount, cacheFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cached results for key {CacheKey}", cacheKey);
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }

    public async Task InvalidateCacheAsync(string cacheKey)
    {
        await _cacheSemaphore.WaitAsync();
        try
        {
            var cacheFilePath = GetCacheFilePath(cacheKey);
            
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
                _logger.LogDebug("Invalidated cache for key {CacheKey}", cacheKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache for key {CacheKey}", cacheKey);
        }
        finally
        {
            _cacheSemaphore.Release();
        }
    }

    private string GetCacheFilePath(string cacheKey)
    {
        return Path.Combine(_cacheDirectory, $"{cacheKey}.json");
    }

    private DateTime GetSolutionLastModified(string solutionPath)
    {
        try
        {
            if (File.Exists(solutionPath))
            {
                return File.GetLastWriteTimeUtc(solutionPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get last modified time for solution {SolutionPath}", solutionPath);
        }
        
        return DateTime.UtcNow;
    }
}