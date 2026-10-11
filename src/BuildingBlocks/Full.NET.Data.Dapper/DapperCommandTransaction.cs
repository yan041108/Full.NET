using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;

namespace Full.NET.Data.Dapper;

/// <summary>最外层命令拥有提交与临时观测，嵌套执行只参与已有事务。</summary>
internal sealed class DapperCommandTransaction(IDbTransactionCoordinator session) : ICommandTransaction, ICommandCommitObserver
{
    private readonly List<Action> _observations = [];
    private bool _ownsTransaction;

    /// <inheritdoc />
    public void Observe(Action observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!_ownsTransaction || !session.HasTransaction)
            throw new InvalidOperationException("Commit observations require an owned command transaction.");
        _observations.Add(observation);
    }

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
        _ownsTransaction = true;
        T result;
        try
        {
            result = await action(cancellationToken).ConfigureAwait(false);
            // 委托或提供者可能忽略令牌；取消后不能提交或发布未提交账本观测。
            cancellationToken.ThrowIfCancellationRequested();
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _observations.Clear();
            await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { _ownsTransaction = false; }
        PublishObservations();
        return result;
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
        _ownsTransaction = true;
        Result<T> result;
        try
        {
            result = await action(cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                _observations.Clear();
                await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                return result;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await session.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _observations.Clear();
            await session.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally { _ownsTransaction = false; }
        PublishObservations();
        return result;
    }

    private void PublishObservations()
    {
        // 先清空本轮队列；监听器失败或重入不能使已提交事实回滚或污染下一轮事务。
        var observations = _observations.ToArray();
        _observations.Clear();
        foreach (var observation in observations)
        {
            try { observation(); }
            catch (Exception) { /* 临时观测允许丢失，不能伪造提交失败或阻断后续观测。 */ }
        }
    }
}
