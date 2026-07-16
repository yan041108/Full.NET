using Full.NET.Abstractions.Messaging;

namespace Full.NET.Data.Dapper;

internal sealed class DapperCommandTransaction(DbSession session) : ICommandTransaction
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
}
