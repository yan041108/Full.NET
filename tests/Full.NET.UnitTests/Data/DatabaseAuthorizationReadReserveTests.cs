using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Data;

/// <summary>普通与 Worker 许可耗尽时，独立授权仍使用自己的有界预留，不形成自等待。</summary>
[TestClass]
public sealed class DatabaseAuthorizationReadReserveTests
{
    [TestMethod]
    public async Task One_normal_transaction_and_one_worker_do_not_block_independent_authorization()
    {
        var settings = new DatabaseCapacityOptions { Enabled = true, HostRole = DatabaseHostRole.Api, PermitLimit = 1,
            CriticalWorkerReserve = 1, AuthorizationReadReserve = 1, QueueLimit = 0, AcquireTimeoutMilliseconds = 250 };
        using var telemetry = new DatabaseConnectionTelemetry(Options.Create(new DatabaseOptions()), Options.Create(settings));
        using var gate = new DatabaseAdmissionGate(Options.Create(settings), telemetry);
        await using var normal = await gate.AcquireAsync(default); await using var worker = await gate.AcquireCriticalAsync(default);
        var authorization = await gate.AcquireAuthorizationAsync(default);
        Assert.AreEqual(3, gate.InUseCount); Assert.AreEqual(0, gate.QueuedCount);
        await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(() => gate.AcquireAuthorizationAsync(default).AsTask());
        await authorization.DisposeAsync(); await authorization.DisposeAsync(); Assert.AreEqual(2, gate.InUseCount);
        await using var next = await gate.AcquireAuthorizationAsync(default); Assert.AreEqual(3, gate.InUseCount);
    }

    [TestMethod]
    public async Task Zero_authorization_reserve_rejects_without_borrowing_free_normal_permit()
    {
        var options = Options.Create(new DatabaseCapacityOptions { Enabled = true, PermitLimit = 1, QueueLimit = 0 });
        using var telemetry = new DatabaseConnectionTelemetry(Options.Create(new DatabaseOptions()), options); using var gate = new DatabaseAdmissionGate(options, telemetry);
        await Assert.ThrowsExactlyAsync<ServiceCapacityExceededException>(() => gate.AcquireAuthorizationAsync(default).AsTask());
        Assert.AreEqual(0, gate.InUseCount); await using var normal = await gate.AcquireAsync(default); Assert.AreEqual(1, gate.InUseCount);
    }

    [TestMethod]
    [DataRow(true, 0, 2, false)] [DataRow(true, 1, 2, true)] [DataRow(false, 0, 1, false)] [DataRow(false, 0, 2, true)]
    public void Read_capacity_includes_actual_provider_pool_and_explicit_admission(bool enabled, int reserve, int pool, bool available)
    {
        var scope = new DatabaseAuthorizationReadScope(Options.Create(new DatabaseCapacityOptions { Enabled = enabled, AuthorizationReadReserve = reserve }),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = $"Server=localhost;Database=test;Max Pool Size={pool}" }));
        Assert.AreEqual(available, scope.HasIndependentReadCapacity);
        if (!available) Assert.ThrowsExactly<ServiceCapacityExceededException>(() => scope.Enter());
        else { using (scope.Enter()) Assert.IsTrue(scope.IsActive); Assert.IsFalse(scope.IsActive); }
    }

    [TestMethod]
    public async Task Cancellation_precedes_authorization_capacity_rejection()
    {
        var options = Options.Create(new DatabaseCapacityOptions { Enabled = true, PermitLimit = 1 });
        using var telemetry = new DatabaseConnectionTelemetry(Options.Create(new DatabaseOptions()), options); using var gate = new DatabaseAdmissionGate(options, telemetry);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => gate.AcquireAuthorizationAsync(cancellation.Token).AsTask());
        Assert.AreEqual(0, gate.InUseCount); Assert.AreEqual(0, gate.QueuedCount);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public void Authorization_reserve_is_included_in_the_same_physical_pool_budget(DatabaseProvider provider)
    {
        var database = new DatabaseOptions { Provider = provider, ConnectionString = provider == DatabaseProvider.SqlServer
            ? "Server=localhost;Database=test;Max Pool Size=2" : "Server=localhost;Database=test;Maximum Pool Size=2" };
        var options = new DatabaseCapacityOptions { Enabled = true, HostRole = DatabaseHostRole.Api, PermitLimit = 1,
            AuthorizationReadReserve = 1, ExpectedMaxPoolSize = 2, ApiMaxReplicas = 1, ApiMaxPoolSize = 2,
            WorkerMaxReplicas = 1, WorkerMaxPoolSize = 2, TotalBudget = 4 };
        var validator = new DatabaseCapacityOptionsValidator(Options.Create(database));
        Assert.IsTrue(validator.Validate(null, options).Succeeded);
        options.AuthorizationReadReserve = 2; Assert.IsTrue(validator.Validate(null, options).Failed);
        options.AuthorizationReadReserve = -1; Assert.IsTrue(validator.Validate(null, options).Failed);
    }
}
