namespace NugetMcp.Core.Services;

public interface IParallelExecutor
{
    Task ExecuteParallelAsync<T>(IEnumerable<T> items, Func<T, Task> action);
    Task<IEnumerable<TResult>> ExecuteParallelAsync<T, TResult>(IEnumerable<T> items, Func<T, Task<TResult>> action);
}