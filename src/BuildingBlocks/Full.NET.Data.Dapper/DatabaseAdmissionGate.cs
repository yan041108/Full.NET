using System.Diagnostics;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

internal sealed class DatabaseAdmissionGate : IDisposable
{
    private readonly DatabaseCapacityOptions _options;
    private readonly DatabaseConnectionTelemetry _telemetry;
    private readonly SemaphoreSlim? _normalSemaphore;
    private readonly SemaphoreSlim? _criticalSemaphore;
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

    private async ValueTask<DatabaseAdmissionLease> AcquireCoreAsync(
        bool critical,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        if (cancellationToken.IsCancellationRequested)
        {
            _telemetry.RecordAcquisition(
                DatabaseConnectionAcquireOutcome.Canceled,
                Stopwatch.GetElapsedTime(startedAt));
            cancellationToken.ThrowIfCancellationRequested();
        }

        var semaphore = critical && _criticalSemaphore is not null
            ? _criticalSemaphore
            : _normalSemaphore;
        var usesCriticalReserve = ReferenceEquals(semaphore, _criticalSemaphore);
        if (semaphore is null)
        {
            return new DatabaseAdmissionLease(
                null,
                usesCriticalReserve: false);
        }

        if (semaphore.Wait(0))
        {
            return CreateLease(usesCriticalReserve);
        }

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

            return CreateLease(usesCriticalReserve);
        }
        finally
        {
            Interlocked.Decrement(ref _queued);
            _telemetry.WaiterDequeued();
        }
    }

    internal void Release(bool usesCriticalReserve)
    {
        Interlocked.Decrement(ref _inUse);
        _telemetry.PermitReleased();
        var semaphore = usesCriticalReserve
            ? _criticalSemaphore
            : _normalSemaphore;
        semaphore!.Release();
    }

    public void Dispose()
    {
        _normalSemaphore?.Dispose();
        _criticalSemaphore?.Dispose();
    }

    private DatabaseAdmissionLease CreateLease(bool usesCriticalReserve)
    {
        Interlocked.Increment(ref _inUse);
        _telemetry.PermitAcquired();
        return new DatabaseAdmissionLease(
            this,
            usesCriticalReserve);
    }

    private ServiceCapacityExceededException CreateCapacityException(
        ServiceCapacityFailureKind kind) => new(
        kind,
        TimeSpan.FromMilliseconds(_options.AcquireTimeoutMilliseconds));
}

internal sealed class DatabaseAdmissionLease(
    DatabaseAdmissionGate? owner,
    bool usesCriticalReserve) : IAsyncDisposable
{
    private DatabaseAdmissionGate? _owner = owner;

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _owner, null)?.Release(usesCriticalReserve);
        return ValueTask.CompletedTask;
    }
}
