using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageHostTenants;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;
using Full.NET.Modules.Tenancy.Features.ManageTenantLifecycle;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Tenancy;

/// <summary>生命周期只在事务门闩之后读取状态；失败 Result 必须回滚锁修订和业务写入。</summary>
[TestClass]
public sealed class TenantLifecycleTransitionTests
{
    [TestMethod]
    public void Lifecycle_read_permission_is_registered_only_for_host_scope()
    {
        var permission = new TenancyAuthorizationContributor().Permissions.Single(item => item.Code == TenancyTenantLifecyclePermissions.Read);
        Assert.AreEqual(AuthorizationScope.Host, permission.Scope);
        Assert.IsFalse(new TenancyAuthorizationContributor().Permissions.Any(item => item.Code == "tenancy.tenant_lifecycle.transfer_ownership"));
    }

    [TestMethod]
    [DataRow("Active", true, "suspend", 2, true)]
    [DataRow("Active", true, "suspend", 1, false)]
    [DataRow("Suspended", false, "close", 1, true)]
    [DataRow("Active", true, "close", 1, false)]
    [DataRow("Suspended", false, "reactivate", 2, true)]
    [DataRow("Closed", false, "reactivate", 2, false)]
    [DataRow("Closed", false, "suspend", 2, false)]
    [DataRow("Closing", false, "close", 2, false)]
    [DataRow("Closed", false, "close", 1, true)]
    public async Task State_machine_preserves_terminal_and_last_active_boundaries(string status, bool active, string action, int activeCount, bool succeeds)
    {
        var f = new Fixture(status, active, activeCount);
        var result = await f.RunAsync(action);
        Assert.AreEqual(succeeds, result.IsSuccess, result.Error?.Code);
        Assert.AreEqual(succeeds ? 1 : 0, f.Coordinator.CommitCount);
        Assert.AreEqual(succeeds ? 0 : 1, f.Coordinator.RollbackCount);
        if (succeeds) Assert.AreEqual(action == "suspend" ? "Suspended" : action == "reactivate" ? "Active" : "Closed", result.Value!.LifecycleStatus);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_CAS_or_result_read_rolls_back_the_whole_transition(bool readFails)
    {
        var f = new Fixture("Active", true, 2) { FailWrite = !readFails, FailResultRead = readFails };
        var result = await f.RunAsync("suspend");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task Stale_version_is_rejected_before_business_writes()
    {
        var f = new Fixture("Active", true, 2);
        var result = await f.Service.SuspendAsync(f.Row.Id, new SuspendTenantRequest(2));
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(TenancyErrorCodes.VersionConflict, result.Error!.Code);
        Assert.IsFalse(f.Written);
    }

    [TestMethod]
    [DataRow("Pending")]
    [DataRow("InProgress")]
    [DataRow("Failed")]
    public async Task Unfinished_provisioning_cannot_be_changed_by_lifecycle_actions(string provisioning)
    {
        var f = new Fixture("Active", true, 2);
        f.Row = f.Row with { ProvisioningStatus = provisioning };
        Assert.IsFalse((await f.RunAsync("close")).IsSuccess);
        Assert.IsFalse(f.Written);
    }

    private sealed class Fixture
    {
        public readonly RecordingDbTransactionCoordinator Coordinator = new();
        public readonly TenantLifecycleManagementService Service;
        public HostTenantRecord Row;
        public bool Written, FailWrite, FailResultRead;
        public Fixture(string status, bool active, int count)
        {
            Row = new HostTenantRecord(Guid.CreateVersion7(), "lifecycle", "Lifecycle", "lifecycle.localhost", active, 1,
                "zh-CN", null, null, null, status, null, "Completed", null);
            var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var guardHeld = false;
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(Coordinator.HasTransaction);
                var sql = call.ArgAt<SqlStatement>(0);
                if (sql == TenantLifecycleWriteGuard.Acquire) { guardHeld = true; return 1; }
                Assert.IsTrue(guardHeld);
                if (FailWrite) return 0;
                var args = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
                Row = Row with { LifecycleStatus = (string)args["LifecycleStatus"]!, IsActive = (bool)args["IsActive"]!, Version = Row.Version + 1 };
                Written = true; return 1;
            });
            query.QuerySingleOrDefaultAsync<HostTenantRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Assert.IsTrue(guardHeld && Coordinator.HasTransaction);
                return Written && FailResultRead ? null : Row;
            });
            query.QuerySingleOrDefaultAsync<TenantResolutionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => new TenantResolutionRecord(Row.Id, Row.Identifier, Row.Name, Row.Domain, Row.IsActive, Row.Version, Row.DefaultLocale));
            query.QuerySingleOrDefaultAsync<long>(TenantSql.CountActiveTenants, cancellationToken: Arg.Any<CancellationToken>()).Returns(_ =>
            { Assert.IsTrue(guardHeld && Coordinator.HasTransaction); return count; });
            Service = new TenantLifecycleManagementService(query, commands, new DapperCommandTransaction(Coordinator),
                new HostTenantQueryService(query, Options.Create(new DatabaseOptions())),
                new TenantCommercialReactivateGate(Options.Create(new TenancyCommercialOptions()), new TenantEntitlementQueryService(query), query),
                clock, new TenantLifecycleWriteGuard(commands));
        }
        public Task<Full.NET.Abstractions.Results.Result<TenantSummary>> RunAsync(string action) => action switch
        {
            "suspend" => Service.SuspendAsync(Row.Id, new SuspendTenantRequest(Row.Version)),
            "reactivate" => Service.ReactivateAsync(Row.Id, new ReactivateTenantRequest(Row.Version)),
            _ => Service.CloseAsync(Row.Id, new CloseTenantRequest(Row.Version))
        };
    }
}
