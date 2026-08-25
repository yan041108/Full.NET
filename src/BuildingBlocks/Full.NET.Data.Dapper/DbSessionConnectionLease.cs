using System.Data.Common;
using System.Diagnostics;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 将连接所有权限定在一次数据库命令内；事务借用租约不负责释放会话持有的连接。
/// </summary>
internal sealed class DbSessionConnectionLease : IAsyncDisposable
{
    private readonly DatabaseConnectionTelemetry? _telemetry;
    private DatabaseAdmissionLease? _admissionLease;
    private DbConnection? _ownedConnection;
    private long _openedAtTimestamp;
    private int _disposed;

    private DbSessionConnectionLease(
        DbConnection connection,
        DbTransaction? transaction,
        DatabaseAdmissionLease? admissionLease,
        DatabaseConnectionTelemetry? telemetry,
        long openedAtTimestamp)
    {
        Connection = connection;
        Transaction = transaction;
        _ownedConnection = admissionLease is null ? null : connection;
        _admissionLease = admissionLease;
        _telemetry = telemetry;
        _openedAtTimestamp = openedAtTimestamp;
    }

    public DbConnection Connection { get; }

    public DbTransaction? Transaction { get; internal set; }

    internal static DbSessionConnectionLease CreateOwned(
        DbConnection connection,
        DatabaseAdmissionLease admissionLease,
        DatabaseConnectionTelemetry telemetry,
        long openedAtTimestamp) => new(
            connection,
            transaction: null,
            admissionLease,
            telemetry,
            openedAtTimestamp);

    internal static DbSessionConnectionLease CreateBorrowed(
        DbConnection connection,
        DbTransaction transaction) => new(
            connection,
            transaction,
            admissionLease: null,
            telemetry: null,
            openedAtTimestamp: 0);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var connection = Interlocked.Exchange(ref _ownedConnection, null);
        if (connection is null)
        {
            return;
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (_openedAtTimestamp != 0)
            {
                _telemetry!.RecordHold(
                    Stopwatch.GetElapsedTime(_openedAtTimestamp));
                _openedAtTimestamp = 0;
            }

            var admissionLease = Interlocked.Exchange(
                ref _admissionLease,
                null);
            if (admissionLease is not null)
            {
                await admissionLease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
