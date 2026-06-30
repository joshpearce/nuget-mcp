using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace NugetMcp.Core.Services;

public class TaskParallelExecutor : IParallelExecutor
{
    private readonly ILogger<TaskParallelExecutor> _logger;
    private readonly int _defaultMaxDegreeOfParallelism;

    public TaskParallelExecutor(ILogger<TaskParallelExecutor> logger)
    {
        _logger = logger;
        _defaultMaxDegreeOfParallelism = GetDefaultMaxDegreeOfParallelism();
        _logger.LogDebug("TaskParallelExecutor initialized with default max degree of parallelism: {DefaultMaxDegreeOfParallelism}", _defaultMaxDegreeOfParallelism);
    }

    private static int GetDefaultMaxDegreeOfParallelism()
    {
        // Check environment variable first
        if (int.TryParse(Environment.GetEnvironmentVariable("NUGET_ANALYZER_MAX_PARALLELISM"), out var envValue) && envValue > 0)
        {
            return envValue;
        }

        // Default to number of physical CPU cores
        return Environment.ProcessorCount;
    }

    public async Task ExecuteParallelAsync<T>(IEnumerable<T> items, Func<T, Task> action)
    {
        var itemList = items.ToList();
        
        _logger.LogDebug("Executing {ItemCount} tasks in parallel with max degree of parallelism: {MaxDegreeOfParallelism}",
            itemList.Count, _defaultMaxDegreeOfParallelism);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _defaultMaxDegreeOfParallelism
        };

        await Parallel.ForEachAsync(itemList, parallelOptions, async (item, ct) =>
        {
            try
            {
                await action(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing parallel task for item: {Item}", item);
                throw;
            }
        });

        _logger.LogDebug("Completed parallel execution of {ItemCount} tasks", itemList.Count);
    }

    public async Task<IEnumerable<TResult>> ExecuteParallelAsync<T, TResult>(IEnumerable<T> items, Func<T, Task<TResult>> action)
    {
        var itemList = items.ToList();
        
        _logger.LogDebug("Executing {ItemCount} tasks in parallel with results, max degree of parallelism: {MaxDegreeOfParallelism}",
            itemList.Count, _defaultMaxDegreeOfParallelism);

        var results = new ConcurrentBag<TResult>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _defaultMaxDegreeOfParallelism
        };

        await Parallel.ForEachAsync(itemList, parallelOptions, async (item, ct) =>
        {
            try
            {
                var result = await action(item);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing parallel task for item: {Item}", item);
                throw;
            }
        });

        var resultList = results.ToList();
        _logger.LogDebug("Completed parallel execution of {ItemCount} tasks with {ResultCount} results", 
            itemList.Count, resultList.Count);
        
        return resultList;
    }
}