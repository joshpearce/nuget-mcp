using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NugetMcp.Core.Services;

public class SourceCodeReader : ISourceCodeReader
{
    private readonly ILogger<SourceCodeReader> _logger;
    private readonly ConcurrentDictionary<string, string[]> _fileCache = new();
    private readonly ConcurrentDictionary<string, DateTime> _fileCacheTimestamps = new();

    public SourceCodeReader(ILogger<SourceCodeReader> logger)
    {
        _logger = logger;
    }

    public async Task<CodeSnippet> ReadCodeSnippetAsync(string filePath, int targetLine, int contextLines)
    {
        try
        {
            if (contextLines < 0)
            {
                throw new ArgumentException("Context lines cannot be negative", nameof(contextLines));
            }

            if (targetLine < 1)
            {
                throw new ArgumentException("Target line must be 1 or greater", nameof(targetLine));
            }

            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found: {FilePath}", filePath);
                return new CodeSnippet
                {
                    Content = $"// File not found: {filePath}",
                    ActualStartLine = targetLine,
                    ActualEndLine = targetLine,
                    WasTruncated = true
                };
            }

            var lines = await GetFileLinesAsync(filePath);
            
            if (lines.Length == 0)
            {
                return new CodeSnippet
                {
                    Content = string.Empty,
                    ActualStartLine = 1,
                    ActualEndLine = 1,
                    WasTruncated = false
                };
            }

            // Convert to 0-based indexing
            var targetIndex = targetLine - 1;
            
            // Validate target line is within file bounds
            if (targetIndex >= lines.Length)
            {
                _logger.LogWarning("Target line {TargetLine} is beyond file length {FileLength} in {FilePath}", 
                    targetLine, lines.Length, filePath);
                return new CodeSnippet
                {
                    Content = $"// Target line {targetLine} is beyond file length ({lines.Length} lines)",
                    ActualStartLine = targetLine,
                    ActualEndLine = targetLine,
                    WasTruncated = true
                };
            }

            // Calculate actual start and end indices
            var startIndex = Math.Max(0, targetIndex - contextLines);
            var endIndex = Math.Min(lines.Length - 1, targetIndex + contextLines);

            // Extract the lines
            var selectedLines = lines.Skip(startIndex).Take(endIndex - startIndex + 1);
            var content = string.Join(Environment.NewLine, selectedLines);

            return new CodeSnippet
            {
                Content = content,
                ActualStartLine = startIndex + 1, // Convert back to 1-based
                ActualEndLine = endIndex + 1,     // Convert back to 1-based
                WasTruncated = startIndex > (targetIndex - contextLines) || endIndex < (targetIndex + contextLines)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading code snippet from {FilePath} at line {TargetLine}", filePath, targetLine);
            return new CodeSnippet
            {
                Content = $"// Error reading file: {ex.Message}",
                ActualStartLine = targetLine,
                ActualEndLine = targetLine,
                WasTruncated = true
            };
        }
    }

    private async Task<string[]> GetFileLinesAsync(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var lastModified = fileInfo.LastWriteTimeUtc;

            // Check if we have a cached version that's still valid
            if (_fileCache.TryGetValue(filePath, out var cachedLines) &&
                _fileCacheTimestamps.TryGetValue(filePath, out var cachedTimestamp) &&
                cachedTimestamp >= lastModified)
            {
                return cachedLines;
            }

            // Check if file is likely binary (basic heuristic)
            if (IsLikelyBinaryFile(filePath))
            {
                _logger.LogWarning("Skipping likely binary file: {FilePath}", filePath);
                return Array.Empty<string>();
            }

            // Read the file
            var content = await File.ReadAllTextAsync(filePath);
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            // Cache the result
            _fileCache[filePath] = lines;
            _fileCacheTimestamps[filePath] = lastModified;

            return lines;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Access denied to file: {FilePath}", filePath);
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file: {FilePath}", filePath);
            return Array.Empty<string>();
        }
    }

    private static bool IsLikelyBinaryFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        // Common binary file extensions
        var binaryExtensions = new[]
        {
            ".dll", ".exe", ".pdb", ".lib", ".obj", ".bin", ".dat",
            ".zip", ".rar", ".7z", ".tar", ".gz",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico",
            ".mp3", ".mp4", ".avi", ".mov", ".wmv",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx"
        };

        return binaryExtensions.Contains(extension);
    }
}