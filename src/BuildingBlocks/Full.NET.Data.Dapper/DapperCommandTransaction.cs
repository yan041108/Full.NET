using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;

namespace Full.NET.Data.Dapper;

internal sealed class DapperCommandTransaction(IDbTransactionCoordinator session) : ICommandTransaction
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (session.HasTransaction)
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }

        await session.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Result<T>> ExecuteResultAsync<T>(
        Func<CancellationToken, Task<Result<T>>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (session.HasTransaction)
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }

        await session.BeginAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await action(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return result;
            }

            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }
}