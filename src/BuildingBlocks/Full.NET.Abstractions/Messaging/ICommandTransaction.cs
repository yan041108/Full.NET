using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

public interface ICommandTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);

    Task<Result<T>> ExecuteResultAsync<T>(
        Func<CancellationToken, Task<Result<T>>> action,
        CancellationToken cancellationToken) =>
        ExecuteAsync(action, cancellationToken);
}