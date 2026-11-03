using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.TenantSeatOperations;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>双库使用真实事务、正式配额适配器和新作用域，验证响应丢失后的恢复及一次性归还。</summary>
[TestClass]
public sealed class TenantSeatRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Lost_confirmation_response_recovers_after_migration_replay_and_removal_refunds_once(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var fault = new LostResponse();
        using var factory = new FullNetApiFactory(provider, connectionString, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Scoped<ITenantMemberSeatQuotaPort>(sp =>
                new LosingSeatPort(new TenantMemberSeatQuotaPort(sp.GetRequiredService<ITenantQuotaReservationService>()), fault))));
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, default);
        var context = new TenantContext(tenant.Id, "acme", "Acme Corporation");
        long before;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accessor = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
            accessor.SetHost();
            before = (await ReadMetricAsync(scope.ServiceProvider, tenant.Id)).UsedValue;
            accessor.SetTenant(context);
            var provision = scope.ServiceProvider.GetRequiredService<TenantMemberProvisionService>();
            await Assert.ThrowsExactlyAsync<IOException>(() => provision.ProvisionAsync(new ProvisionTenantMemberRequest(
                "recovery-" + Guid.NewGuid().ToString("N")[..12], "Recovery Member", FullNetApiFactory.TestPassword, TenantMemberRoles.Member, null)));
        }
        var operationId = Guid.Parse(fault.OperationId!.Split(':')[0]);

        // DbUp 未记账重跑不得丢失已有工作项；SQL Server 同时模拟建表后索引尚未创建。
        await using (DbConnection connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString))
        {
            await connection.OpenAsync();
            if (provider == DatabaseProvider.SqlServer)
                await connection.ExecuteAsync("DROP INDEX IX_fn_identity_member_seat_operation_Phase_DueAtUtc ON fn_identity_member_seat_operation");
            await ReviewFixMigrationRecoverySupport.DeleteScriptAsync(connection, "324_IdentityMemberSeatOperations.sql", provider == DatabaseProvider.SqlServer);
            await ReviewFixMigrationRecoverySupport.CreateRunner(provider, connectionString).MigrateAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var accessor = sp.GetRequiredService<CurrentTenantAccessor>();
            accessor.SetHost();
            var queries = sp.GetRequiredService<IQueryExecutor>();
            var op = await queries.QuerySingleOrDefaultAsync<TenantSeatOperation>(TenantSeatOperationSql.Find,
                IdentitySqlParameters.Create(("Id", operationId)));
            Assert.IsNotNull(op);
            Assert.AreEqual(TenantSeatPhases.Reserved, op.Phase);
            Assert.AreEqual(before + 1, (await ReadMetricAsync(sp, tenant.Id)).UsedValue);
            accessor.SetTenant(context);
            var memberQueries = sp.GetRequiredService<TenantMembershipQueryService>();
            var pending = await memberQueries.GetMemberByIdAsync(op.MemberId);
            Assert.AreEqual(TenantMemberStatuses.Pending, pending.Value!.Status);
            var processor = sp.GetRequiredService<TenantSeatOperationProcessor>();
            await using (DbConnection first = provider == DatabaseProvider.SqlServer
                ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString))
            await using (DbConnection second = provider == DatabaseProvider.SqlServer
                ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString))
            {
                await first.OpenAsync();
                await second.OpenAsync();
                var claims = await Task.WhenAll(first.ExecuteAsync(TenantSeatOperationSql.Acquire.Text,
                        new { Id = operationId, LeaseId = Guid.CreateVersion7(), Now = DateTime.UtcNow, Until = DateTime.UtcNow.AddMinutes(1) }),
                    second.ExecuteAsync(TenantSeatOperationSql.Acquire.Text,
                        new { Id = operationId, LeaseId = Guid.CreateVersion7(), Now = DateTime.UtcNow, Until = DateTime.UtcNow.AddMinutes(1) }));
                Assert.AreEqual(1, claims.Sum());
                var busy = await processor.ProcessAsync(operationId, default);
                Assert.IsTrue(busy.IsSuccess);
                Assert.IsFalse(busy.Value);
                await first.ExecuteAsync("UPDATE fn_identity_member_seat_operation SET LeaseUntilUtc=@Expired WHERE Id=@Id",
                    new { Id = operationId, Expired = DateTime.UtcNow.AddMinutes(-1) });
            }
            Assert.IsTrue((await processor.ProcessAsync(operationId, default)).Value);
            Assert.IsTrue((await processor.ProcessAsync(operationId, default)).Value);
            var active = await memberQueries.GetMemberByIdAsync(op.MemberId);
            Assert.AreEqual(TenantMemberStatuses.Active, active.Value!.Status);
            var removed = await sp.GetRequiredService<TenantMembershipManagementService>()
                .RemoveMemberAsync(op.MemberId, active.Value.Version);
            Assert.IsTrue(removed.IsSuccess, removed.Error?.Code);
            await processor.ProcessAsync(operationId, default);
            await processor.ProcessAsync(operationId, default);
            Assert.AreEqual(TenantMemberStatuses.Removed, (await memberQueries.GetMemberByIdAsync(op.MemberId)).Value!.Status);
            accessor.SetHost();
            Assert.AreEqual(before, (await ReadMetricAsync(sp, tenant.Id)).UsedValue);
            var terminal = await queries.QuerySingleOrDefaultAsync<TenantSeatOperation>(TenantSeatOperationSql.Find,
                IdentitySqlParameters.Create(("Id", operationId)));
            Assert.AreEqual(TenantSeatPhases.Cancelled, terminal!.Phase);
            var late = await sp.GetRequiredService<ITenantQuotaReservationService>().ConfirmAsync(tenant.Id,
                new ConfirmTenantQuotaRequest(fault.OperationId) { MetricCode = TenantQuotaMetricCodes.IdentitySeats });
            Assert.IsFalse(late.IsSuccess);
        }
        await VerifyLastSeatAndCreationReplayAsync(factory, context, provider, connectionString, before);
    }

    private static async Task VerifyLastSeatAndCreationReplayAsync(FullNetApiFactory factory, TenantContext tenant,
        DatabaseProvider provider, string connectionString, long baseline)
    {
        await using DbConnection connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("UPDATE fn_tenancy_quota_metric SET LimitValue=@Limit,Version=Version+1 WHERE TenantId=@TenantId AND MetricCode='identity.seats' AND PeriodKey=@PeriodKey",
            new { Limit = baseline + 1, TenantId = tenant.Id, PeriodKey = TenantQuotaDefaults.PeriodKey });
        var requests = Enumerable.Range(0, 2).Select(_ => new ProvisionTenantMemberRequest(
            "lastseat-" + Guid.NewGuid().ToString("N")[..12], "Last Seat", FullNetApiFactory.TestPassword, TenantMemberRoles.Member, null)).ToArray();
        async Task<Result<TenantMemberResponse>> ProvisionAsync(ProvisionTenantMemberRequest request)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(tenant);
            return await scope.ServiceProvider.GetRequiredService<TenantMemberProvisionService>().ProvisionAsync(request);
        }
        var results = await Task.WhenAll(requests.Select(ProvisionAsync));
        Assert.AreEqual(1, results.Count(r => r.IsSuccess && r.Value!.Status == TenantMemberStatuses.Active));
        var failed = Array.FindIndex(results, r => !r.IsSuccess);
        Assert.IsGreaterThanOrEqualTo(0, failed);
        Assert.AreEqual(TenancyErrorCodes.QuotaExceeded, results[failed].Error!.Code);
        await connection.ExecuteAsync("UPDATE fn_tenancy_quota_metric SET LimitValue=@Limit,Version=Version+1 WHERE TenantId=@TenantId AND MetricCode='identity.seats' AND PeriodKey=@PeriodKey",
            new { Limit = baseline + 2, TenantId = tenant.Id, PeriodKey = TenantQuotaDefaults.PeriodKey });
        var recovered = await ProvisionAsync(requests[failed]);
        Assert.IsTrue(recovered.IsSuccess, recovered.Error?.Code);
        Assert.AreEqual(TenantMemberStatuses.Active, recovered.Value!.Status);
        var replay = await ProvisionAsync(requests[failed]);
        Assert.IsTrue(replay.IsSuccess, replay.Error?.Code);
        Assert.AreEqual(recovered.Value.Id, replay.Value!.Id);
        Assert.AreEqual(baseline + 2, await connection.QuerySingleAsync<long>(
            "SELECT UsedValue FROM fn_tenancy_quota_metric WHERE TenantId=@TenantId AND MetricCode='identity.seats' AND PeriodKey=@PeriodKey",
            new { TenantId = tenant.Id, PeriodKey = TenantQuotaDefaults.PeriodKey }));
    }

    private static async Task<TenantQuotaMetricRecord> ReadMetricAsync(IServiceProvider sp, Guid tenantId) =>
        (await sp.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<TenantQuotaMetricRecord>(TenantQuotaSql.FindMetric,
            new Dictionary<string, object?> { ["TenantId"] = tenantId, ["MetricCode"] = TenantQuotaMetricCodes.IdentitySeats,
                ["PeriodKey"] = TenantQuotaDefaults.PeriodKey }))!;

    private sealed class LostResponse { internal string? OperationId; }

    private sealed class LosingSeatPort(ITenantMemberSeatQuotaPort inner, LostResponse fault) : ITenantMemberSeatQuotaPort
    {
        public Task<Result<bool>> TryReserveAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) => inner.TryReserveAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> ReleaseAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) => inner.ReleaseAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> CancelConsumptionAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) => inner.CancelConsumptionAsync(tenantId, operationId, cancellationToken);
        public async Task<Result<bool>> ConfirmAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default)
        {
            var result = await inner.ConfirmAsync(tenantId, operationId, cancellationToken);
            if (result.IsSuccess && fault.OperationId is null)
            {
                fault.OperationId = operationId;
                throw new IOException("Quota committed; response lost.");
            }
            return result;
        }
    }
}
