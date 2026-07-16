using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

public interface ICommand<TResult>;

public interface ITransactionalCommand;

public interface ITransactionalCommand<TResult> : ICommand<TResult>, ITransactionalCommand;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken);
}

public interface ICommandDispatcher
{
    Task<Result<TResult>> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
