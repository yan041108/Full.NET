using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modularity.Messaging;

public sealed class CommandDispatcher(
    IServiceProvider services,
    ICommandTransaction? transaction = null) : ICommandDispatcher
{
    public Task<Result<TResult>> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        DispatchHandlerDelegate<TResult> pipeline =
            ct => HandleCoreAsync<TCommand, TResult>(command, ct);

        foreach (var behavior in services
                     .GetServices<IDispatchBehavior<TCommand, TResult>>()
                     .Reverse())
        {
            var next = pipeline;
            pipeline = ct => behavior.HandleAsync(command, next, ct);
        }

        return pipeline(cancellationToken);
    }

    private Task<Result<TResult>> HandleCoreAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        var handler = services.GetRequiredService<ICommandHandler<TCommand, TResult>>();

        if (command is ITransactionalCommand)
        {
            return (transaction ?? throw new InvalidOperationException(
                    "No command transaction is registered."))
                .ExecuteAsync(
                    ct => handler.HandleAsync(command, ct),
                    cancellationToken);
        }

        return handler.HandleAsync(command, cancellationToken);
    }
}
