using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class DatabaseAdmissionGateTests
{
    [TestMethod]
    public async Task AcquireAsync_RejectsImmediatelyWhenZeroQueueIsFull()
    {
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry, queueLimit: 0);
        await using var first = await gate.AcquireAsync(CancellationToken.None);

        var exception = await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(
            () => gate.AcquireAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(ServiceCapacityFailureKind.Rejected, exception.Kind);
        Assert.AreEqual(1, gate.InUseCount);
        Assert.AreEqual(0, gate.QueuedCount);
    }

    [TestMethod]
    public async Task AcquireAsync_QueuesOneWaiterAndRecoversAfterRelease()
    {
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry, queueLimit: 1);
        var first = await gate.AcquireAsync(CancellationToken.None);

        var waiting = gate.AcquireAsync(CancellationToken.None).AsTask();
        await WaitUntilAsync(() => gate.QueuedCount == 1);
        Assert.IsFalse(waiting.IsCompleted);

        await first.DisposeAsync();
        await using var second = await waiting;

        Assert.AreEqual(1, gate.InUseCount);
        Assert.AreEqual(0, gate.QueuedCount);
    }

    [TestMethod]
    public async Task AcquireAsync_RejectsWaitersBeyondQueueLimit()
    {
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry, queueLimit: 1);
        var first = await gate.AcquireAsync(CancellationToken.None);
        var queued = gate.AcquireAsync(CancellationToken.None).AsTask();
        await WaitUntilAsync(() => gate.QueuedCount == 1);

        var exception = await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(
            () => gate.AcquireAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(ServiceCapacityFailureKind.Rejected, exception.Kind);
        await first.DisposeAsync();
        await (await queued).DisposeAsync();
    }

    [TestMethod]
    public async Task AcquireAsync_TimesOutAndAllowsLaterRecovery()
    {
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(
            telemetry,
            queueLimit: 1,
            acquireTimeoutMilliseconds: 30);
        await using var first = await gate.AcquireAsync(CancellationToken.None);

        var exception = await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(
            () => gate.AcquireAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(ServiceCapacityFailureKind.Timeout, exception.Kind);
        Assert.AreEqual(0, gate.QueuedCount);
        await first.DisposeAsync();
        await using var recovered = await gate.AcquireAsync(CancellationToken.None);
        Assert.AreEqual(1, gate.InUseCount);
    }

    [TestMethod]
    public async Task AcquireAsync_PropagatesCallerCancellationWithoutCapacityException()
    {
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry, queueLimit: 1);
        await using var first = await gate.AcquireAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = gate.AcquireAsync(cancellation.Token).AsTask();
        await WaitUntilAsync(() => gate.QueuedCount == 1);

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
        Assert.AreEqual(0, gate.QueuedCount);
    }

    [TestMethod]
    public async Task AcquireAsync_DoesNotConsumeAvailablePermitWhenAlreadyCanceled()
    {
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry, queueLimit: 0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => gate.AcquireAsync(cancellation.Token).AsTask());

        Assert.AreEqual(0, gate.InUseCount);
        await using var recovered = await gate.AcquireAsync(CancellationToken.None);
        Assert.AreEqual(1, gate.InUseCount);
    }

    [TestMethod]
    public async Task Lease_DisposeIsIdempotent()
    {
        using var telemetry = CreateTelemetry();
        using var gate = CreateGate(telemetry, queueLimit: 0);
        var lease = await gate.AcquireAsync(CancellationToken.None);

        await lease.DisposeAsync();
        await lease.DisposeAsync();
        await using var next = await gate.AcquireAsync(CancellationToken.None);

        Assert.AreEqual(1, gate.InUseCount);
    }

    [TestMethod]
    public async Task AcquireCriticalAsync_UsesReservedPermitWhenNormalAdmissionIsFull()
    {
        using var telemetry = CreateTelemetry();
        using var gate = new DatabaseAdmissionGate(
            Options.Create(new DatabaseCapacityOptions
            {
                Enabled = true,
                HostRole = DatabaseHostRole.Worker,
                PermitLimit = 1,
                CriticalWorkerReserve = 1,
                QueueLimit = 0,
                AcquireTimeoutMilliseconds = 100,
            }),
            telemetry);
        await using var normal = await gate.AcquireAsync(CancellationToken.None);

        await using var critical = await gate.AcquireCriticalAsync(
            CancellationToken.None);

        Assert.AreEqual(2, gate.InUseCount);
        await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(
            () => gate.AcquireAsync(CancellationToken.None).AsTask());
    }

    private static DatabaseAdmissionGate CreateGate(
        DatabaseConnectionTelemetry telemetry,
        int queueLimit,
        int acquireTimeoutMilliseconds = 2_000) => new(
        Options.Create(new DatabaseCapacityOptions
        {
            Enabled = true,
            HostRole = DatabaseHostRole.Api,
            PermitLimit = 1,
            QueueLimit = queueLimit,
            AcquireTimeoutMilliseconds = acquireTimeoutMilliseconds,
        }),
        telemetry);

    private static DatabaseConnectionTelemetry CreateTelemetry() => new(
        Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
        }),
        Options.Create(new DatabaseCapacityOptions
        {
            HostRole = DatabaseHostRole.Api,
        }));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
