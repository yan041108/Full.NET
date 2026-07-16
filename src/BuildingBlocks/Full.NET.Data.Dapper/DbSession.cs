using System.Data;
using System.Data.Common;

namespace Full.NET.Data.Dapper;

internal sealed class DbSession(DbConnectionFactory connectionFactory) : IAsyncDisposable
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;

    public DbTransaction? Transaction => _transaction;

    public bool HasTransaction => _transaction is not null;

    public async Task<DbConnection> GetOpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        _connection ??= connectionFactory.Create();
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        return _connection;
    }

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A database transaction is already active.");
        }

        var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        _transaction = await connection
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction ?? throw new InvalidOperationException(
            "No database transaction is active.");

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        var transaction = _transaction;
        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }
}
