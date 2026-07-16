namespace Full.NET.Data.Abstractions;

public interface IQueryExecutor
{
    Task<T?> QuerySingleOrDefaultAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
