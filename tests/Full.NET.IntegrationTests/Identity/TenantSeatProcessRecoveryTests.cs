using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ReserveTenantQuota;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实 Worker 确认席位后中断，新进程等待原租约到期并接续成员激活。</summary>
[TestClass]
public sealed class TenantSeatProcessRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Worker_crash_after_seat_confirmation_recovers_without_duplicate_consumption(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var fault = new BeforeConfirmation();
        using var factory = new FullNetApiFactory(provider, connectionString, configureTestServices: services =>
            services.Replace(ServiceDescriptor.Scoped<ITenantMemberSeatQuotaPort>(sp =>
                new ConfirmationUnavailable(new TenantMemberSeatQuotaPort(sp.GetRequiredService<ITenantQuotaReservationService>()), fault))));
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client, default);
        var context = new TenantContext(tenant.Id, "acme", "Acme Corporation");
        await using DbConnection db = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await db.OpenAsync();
        var before = await UsageAsync();
        var username = "process-seat-" + Guid.NewGuid().ToString("N")[..12];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetTenant(context);
            await Assert.ThrowsExactlyAsync<IOException>(() =>
                scope.ServiceProvider.GetRequiredService<TenantMemberProvisionService>().ProvisionAsync(
                    new ProvisionTenantMemberRequest(username, "Process Recovery", FullNetApiFactory.TestPassword, TenantMemberRoles.Member, null)));
        }
        Assert.IsNotNull(fault.OperationId);
        var operationId = Guid.Parse(fault.OperationId.Split(':')[0]);
        var operation = await ReadOperationAsync();
        Assert.AreEqual("Reserved", operation.Phase);
        Assert.AreEqual(TenantMemberStatuses.Pending, await MemberStatusAsync(operation.MemberId));
        var reserved = await UsageAsync();
        Assert.AreEqual(before.Used, reserved.Used);
        Assert.AreEqual(before.Reserved + 1, reserved.Reserved);

        var evidence = Directory.CreateTempSubdirectory("fullnet-seat-process-").FullName;
        var passed = false;
        try
        {
            var gate = Path.Combine(evidence, "confirmation.gate");
            int firstId;
            await using (var first = await EnterpriseRequestWorkerProcess.StartAsync(factory, evidence, "seat-confirm", gate, 1))
            {
                firstId = first.Id;
                await UntilAsync(() => Task.FromResult(File.Exists(gate)));
                Assert.AreEqual(fault.OperationId, await File.ReadAllTextAsync(gate));
                operation = await ReadOperationAsync();
                Assert.AreEqual("Reserved", operation.Phase);
                Assert.IsNotNull(operation.LeaseId);
                Assert.AreEqual(1, operation.LeaseIsCurrent);
                Assert.AreEqual(TenantMemberStatuses.Pending, await MemberStatusAsync(operation.MemberId));
                var confirmed = await UsageAsync();
                Assert.AreEqual(before.Used + 1, confirmed.Used);
                Assert.AreEqual(before.Reserved, confirmed.Reserved);
                await first.CrashAsync();
            }

            var stranded = await ReadOperationAsync();
            Assert.AreEqual(operation.LeaseId, stranded.LeaseId);
            Assert.AreEqual("Reserved", stranded.Phase);
            await using (var second = await EnterpriseRequestWorkerProcess.StartAsync(factory, evidence, "seat-recovery", gate, 2))
            {
                Assert.AreNotEqual(firstId, second.Id);
                // 不修改租约或直接调用恢复器，等待生产 Worker 自然接续。
                await UntilAsync(async () =>
                {
                    var current = await ReadOperationAsync();
                    return current.Phase == "Activated" && current.LeaseId is null;
                });
                Assert.AreEqual(TenantMemberStatuses.Active, await MemberStatusAsync(operation.MemberId));
                var recovered = await UsageAsync();
                Assert.AreEqual(before.Used + 1, recovered.Used);
                Assert.AreEqual(before.Reserved, recovered.Reserved);
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM fn_identity_tenant_member WHERE TenantId=@Tenant AND Id=@Member AND Status='Active'",
                    new { Tenant = tenant.Id, Member = operation.MemberId }));
            }
            passed = true;
        }
        finally
        {
            var keys = Path.Combine(evidence, "keys");
            if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true);
            if (passed) Directory.Delete(evidence, recursive: true);
            else Console.WriteLine("席位进程恢复失败记录：" + evidence);
        }

        Task<(long Used, long Reserved)> UsageAsync() => db.QuerySingleAsync<(long Used, long Reserved)>(
            "SELECT UsedValue,ReservedValue FROM fn_tenancy_quota_metric WHERE TenantId=@Tenant AND MetricCode='identity.seats' AND PeriodKey='default'",
            new { Tenant = tenant.Id });
        Task<(string Phase, Guid MemberId, Guid? LeaseId, int LeaseIsCurrent)> ReadOperationAsync() =>
            db.QuerySingleAsync<(string Phase, Guid MemberId, Guid? LeaseId, int LeaseIsCurrent)>(
                "SELECT Phase,MemberId,LeaseId,CASE WHEN LeaseUntilUtc>@Now THEN 1 ELSE 0 END FROM fn_identity_member_seat_operation WHERE Id=@Id",
                new { Id = operationId, Now = DateTime.UtcNow });
        Task<string> MemberStatusAsync(Guid id) => db.QuerySingleAsync<string>(
            "SELECT Status FROM fn_identity_tenant_member WHERE TenantId=@Tenant AND Id=@Id", new { Tenant = tenant.Id, Id = id });
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        while (!await condition()) await Task.Delay(100, timeout.Token);
    }

    private sealed class BeforeConfirmation { internal string? OperationId; }

    private sealed class ConfirmationUnavailable(ITenantMemberSeatQuotaPort original, BeforeConfirmation fault) : ITenantMemberSeatQuotaPort
    {
        public Task<Result<bool>> TryReserveAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) =>
            original.TryReserveAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> ReleaseAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) =>
            original.ReleaseAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> CancelConsumptionAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default) =>
            original.CancelConsumptionAsync(tenantId, operationId, cancellationToken);
        public Task<Result<bool>> ConfirmAsync(Guid tenantId, string operationId, CancellationToken cancellationToken = default)
        {
            fault.OperationId = operationId;
            throw new IOException("Seat confirmation unavailable before dispatch.");
        }
    }
}
