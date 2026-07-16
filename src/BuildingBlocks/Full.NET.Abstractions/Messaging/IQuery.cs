using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<Result<TResult>> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken);
}

public interface IQueryDispatcher
{
    Task<Result<TResult>> SendAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>;
}
