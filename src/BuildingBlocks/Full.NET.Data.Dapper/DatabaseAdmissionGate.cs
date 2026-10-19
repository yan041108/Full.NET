using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 进程内数据库连接准入：限制并发许可、排队上限与 Worker 关键预留；拒绝与超时映射为容量异常。
/// </summary>
internal sealed class DatabaseAdmissionGate : IDisposable
{
    private readonly DatabaseCapacityOptions _options;
    private readonly DatabaseConnectionTelemetry _telemetry;
    private readonly SemaphoreSlim? _normalSemaphore;
    private readonly SemaphoreSlim? _criticalSemaphore;
    private readonly SemaphoreSlim? _authorizationSemaphore;
    private int _inUse;
    private int _queued;

    public DatabaseAdmissionGate(
        IOptions<DatabaseCapacityOptions> options,
        DatabaseConnectionTelemetry telemetry)
    {
        _options = options.Value;
        _telemetry = telemetry;
        if (_options.Enabled)
        {
            _normalSemaphore = new SemaphoreSlim(
                _options.PermitLimit,
                _options.PermitLimit);
            if (_options.AuthorizationReadReserve > 0)
                _authorizationSemaphore = new(_options.AuthorizationReadReserve, _options.AuthorizationReadReserve);
            if (_options.CriticalWorkerReserve > 0)
            {
                _criticalSemaphore = new SemaphoreSlim(
                    _options.CriticalWorkerReserve,
                    _options.CriticalWorkerReserve);
            }
        }
    }

    internal int InUseCount => Volatile.Read(ref _inUse);

    internal int QueuedCount => Volatile.Read(ref _queued);

    internal async ValueTask<DatabaseAdmissionLease> AcquireAsync(
        CancellationToken cancellationToken) =>
        await AcquireCoreAsync(
                critical: false,
                cancellationToken)
            .ConfigureAwait(false);

    internal async ValueTask<DatabaseAdmissionLease> AcquireCriticalAsync(
        CancellationToken cancellationToken) =>
        await AcquireCoreAsync(
                critical: true,
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>独立授权使用自己的有界预留，禁止借用普通或 Worker 配额形成循环等待。</summary>
    internal ValueTask<DatabaseAdmissionLease> AcquireAuthorizationAsync(CancellationToken cancellationToken) =>
        AcquireCoreAsync(false, cancellationToken, authorization: true);

    private async ValueTask<DatabaseAdmissionLease> AcquireCoreAsync(
        bool critical,
        CancellationToken cancellationToken, bool authorization = false)
    {
        var startedAt = Stopwatch.GetTimestamp();
        if (cancellationToken.IsCancellationRequested)
        {
            _telemetry.RecordAcquisition(
                DatabaseConnectionAcquireOutcome.Canceled,
                Stopwatch.GetElapsedTime(startedAt));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (authorization && _options.Enabled && _authorizationSemaphore is null)
            throw CreateCapacityException(ServiceCapacityFailureKind.Rejected);
        var semaphore = authorization && _authorizationSemaphore is not null ? _authorizationSemaphore
            : critical && _criticalSemaphore is not null
            ? _criticalSemaphore
            : _normalSemaphore;
        var usesCriticalReserve = ReferenceEquals(semaphore, _criticalSemaphore);
        var usesAuthorizationReserve = authorization && _authorizationSemaphore is not null;
        if (semaphore is null)
        {
            return new DatabaseAdmissionLease(
                null,
                usesCriticalReserve: false);
        }

        if (semaphore.Wait(0))
        {
            return CreateLease(usesCriticalReserve, usesAuthorizationReserve);
        }

        // QueueLimit=0 表示不排队，许可耗尽立即拒绝，避免无界等待拖垮线程池。
        if (_options.QueueLimit == 0
            || Interlocked.Increment(ref _queued) > _options.QueueLimit)
        {
            if (_options.QueueLimit != 0)
            {
                Interlocked.Decrement(ref _queued);
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            _telemetry.RecordAcquisition(
                DatabaseConnectionAcquireOutcome.Rejected,
                elapsed);
            throw CreateCapacityException(ServiceCapacityFailureKind.Rejected);
        }

        _telemetry.WaiterQueued();
        try
        {
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(_options.AcquireTimeoutMilliseconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token);
            try
            {
                await semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _telemetry.RecordAcquisition(
                    DatabaseConnectionAcquireOutcome.Canceled,
                    Stopwatch.GetElapsedTime(startedAt));
                throw;
            }
            catch (OperationCanceledException)
            {
                _telemetry.RecordAcquisition(
                    DatabaseConnectionAcquireOutcome.Timeout,
                    Stopwatch.GetElapsedTime(startedAt));
                throw CreateCapacityException(ServiceCapacityFailureKind.Timeout);
            }

            return CreateLease(usesCriticalReserve, usesAuthorizationReserve);
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
            _telemetry.WaiterDequeued();
        }
    }

    internal void Release(bool usesCriticalReserve, bool usesAuthorizationReserve = false)
    {
        Interlocked.Decrement(ref _inUse);
        _telemetry.PermitReleased();
        var semaphore = usesAuthorizationReserve ? _authorizationSemaphore : usesCriticalReserve
            ? _criticalSemaphore
            : _normalSemaphore;
        semaphore!.Release();
    }

    public void Dispose()
    {
        _normalSemaphore?.Dispose();
        _criticalSemaphore?.Dispose();
        _authorizationSemaphore?.Dispose();
    }

    private DatabaseAdmissionLease CreateLease(bool usesCriticalReserve, bool usesAuthorizationReserve = false)
    {
        Interlocked.Increment(ref _inUse);
        _telemetry.PermitAcquired();
        return new DatabaseAdmissionLease(
            this,
            usesCriticalReserve, usesAuthorizationReserve);
    }

    private ServiceCapacityExceededException CreateCapacityException(
        ServiceCapacityFailureKind kind) => new(
        kind,
        TimeSpan.FromMilliseconds(_options.AcquireTimeoutMilliseconds));
}

/// <summary>释放时归还许可；异步释放避免在 finally 中阻塞线程池。</summary>
internal sealed class DatabaseAdmissionLease(
    DatabaseAdmissionGate? owner,
    bool usesCriticalReserve, bool usesAuthorizationReserve = false) : IAsyncDisposable
{
    private DatabaseAdmissionGate? _owner = owner;

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _owner, null)?.Release(usesCriticalReserve, usesAuthorizationReserve);
        return ValueTask.CompletedTask;
    }
}
