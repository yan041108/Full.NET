namespace Full.NET.Abstractions.Messaging;

public interface ICommandTransaction
{
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);
}
