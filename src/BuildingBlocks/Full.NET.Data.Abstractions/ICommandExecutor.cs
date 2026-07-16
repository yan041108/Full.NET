namespace Full.NET.Data.Abstractions;

public interface ICommandExecutor
{
    Task<int> ExecuteAsync(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
