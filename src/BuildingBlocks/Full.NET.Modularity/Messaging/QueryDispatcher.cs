using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Messaging;

public sealed class QueryDispatcher(IServiceProvider services) : IQueryDispatcher
{
    public Task<Result<TResult>> SendAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>
    {
        DispatchHandlerDelegate<TResult> pipeline =
            ct => HandleCoreAsync<TQuery, TResult>(query, ct);

        foreach (var behavior in services
                     .GetServices<IDispatchBehavior<TQuery, TResult>>()
                     .Reverse())
        {
            var next = pipeline;
            pipeline = ct => behavior.HandleAsync(query, next, ct);
        }

        return pipeline(cancellationToken);
    }

    private Task<Result<TResult>> HandleCoreAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken)
        where TQuery : IQuery<TResult> =>
        services.GetRequiredService<IQueryHandler<TQuery, TResult>>()
            .HandleAsync(query, cancellationToken);
}
