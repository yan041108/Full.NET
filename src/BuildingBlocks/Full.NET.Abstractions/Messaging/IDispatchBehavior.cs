using Full.NET.Abstractions.Results;

namespace Full.NET.Abstractions.Messaging;

public delegate Task<Result<TResult>> DispatchHandlerDelegate<TResult>(
    CancellationToken cancellationToken);

public interface IDispatchBehavior<in TMessage, TResult>
{
    Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken);
}
