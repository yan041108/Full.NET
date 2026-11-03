using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>沿真实详情服务调用链观察事务状态，防止间接调用把 Identity 带入组织事务。</summary>
[TestClass]
public sealed class OrganizationAssignmentTransactionBoundaryTests
{
    [TestMethod]
    [DataRow("unit", "create")]
    [DataRow("unit", "reactivate")]
    [DataRow("unit", "update")]
    [DataRow("unit", "disable")]
    [DataRow("position", "create")]
    [DataRow("position", "update")]
    [DataRow("position", "disable")]
    public async Task Management_uses_display_snapshot_and_keeps_local_reads_and_writes_in_transaction(string kind, string operation)
    {
        var harness = new Harness(kind, operation);
        var result = await harness.RunAsync();

        Assert.IsTrue(result.Success, result.Error?.Code);
        Assert.AreEqual("before-transaction", result.Username);
        Assert.AreEqual(2, result.Version);
        Assert.AreEqual(1, harness.Transaction.Commits);
        Assert.AreEqual(0, harness.Transaction.Rollbacks);
        Assert.IsGreaterThan(0, harness.LocalTransactionalReads);
        Assert.IsGreaterThan(0, harness.Transaction.CommittedWrites);
        Assert.AreEqual(operation is "create" or "reactivate" ? 0 : 1, harness.DisplayReads);
    }

    [TestMethod]
    [DataRow("unit", "update", false)]
    [DataRow("unit", "update", true)]
    [DataRow("unit", "disable", false)]
    [DataRow("unit", "disable", true)]
    [DataRow("position", "update", false)]
    [DataRow("position", "update", true)]
    [DataRow("position", "disable", false)]
    [DataRow("position", "disable", true)]
    public async Task Display_failure_or_missing_user_prevents_transaction_and_writes(string kind, string operation, bool throws)
    {
        var harness = new Harness(kind, operation) { MissingDisplay = !throws, ThrowDisplay = throws };
        if (throws)
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => harness.RunAsync());
        else
            Assert.IsFalse((await harness.RunAsync()).Success);

        Assert.AreEqual(1, harness.DisplayReads);
        Assert.AreEqual(0, harness.Transaction.Executions);
        Assert.AreEqual(0, harness.Transaction.CommittedWrites);
        await harness.Command.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    [DataRow("unit")]
    [DataRow("position")]
    public async Task Version_conflict_still_rolls_back_preceding_primary_flag_write(string kind)
    {
        var harness = new Harness(kind, "update") { Conflict = true };
        var result = await harness.RunAsync();

        Assert.IsFalse(result.Success);
        Assert.AreEqual(IdentityErrorCodes.ProfileVersionConflict, result.Error!.Code);
        Assert.AreEqual(1, harness.Transaction.Rollbacks);
        Assert.AreEqual(0, harness.Transaction.Commits);
        Assert.AreEqual(0, harness.Transaction.CommittedWrites);
        Assert.AreEqual(1, harness.DisplayReads);
    }

    private sealed record Outcome(bool Success, Error? Error, string? Username, int Version);

    private sealed class Harness
    {
        private readonly string _kind;
        private readonly string _operation;
        private readonly Guid _user = Guid.CreateVersion7();
        private readonly Guid _assignment = Guid.CreateVersion7();
        private readonly Guid _object = Guid.CreateVersion7();
        private readonly Guid _tenant = Guid.CreateVersion7();
        private readonly IQueryExecutor _query = Substitute.For<IQueryExecutor>();
        private readonly ITenantMemberSelectionDirectory _members = Substitute.For<ITenantMemberSelectionDirectory>();
        private readonly IHostUserDisplayDirectory _display = Substitute.For<IHostUserDisplayDirectory>();
        internal BoundaryTransaction Transaction { get; } = new();
        internal ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        internal int DisplayReads { get; private set; }
        internal int LocalTransactionalReads { get; private set; }
        internal bool MissingDisplay { get; init; }
        internal bool ThrowDisplay { get; init; }
        internal bool Conflict { get; init; }

        internal Harness(string kind, string operation)
        {
            (_kind, _operation) = (kind, operation);
            _members.FindActiveTenantMemberAsync(_user, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Assert.IsFalse(Transaction.HasTransaction);
                return new TenantUserDirectoryEntry(_user, "before-transaction", "事务前姓名");
            });
            _display.FindHostUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Assert.IsFalse(Transaction.HasTransaction, "详情服务的间接目录调用不得加入本地事务。");
                Assert.AreEqual(0, Transaction.Executions, "提交后不得再读取展示信息并把已成功写入报告为失败。");
                DisplayReads++;
                if (ThrowDisplay) throw new InvalidOperationException("display unavailable");
                return MissingDisplay ? new Dictionary<Guid, HostUserDirectoryEntry>()
                    : new Dictionary<Guid, HostUserDirectoryEntry> { [_user] = new(_user, "before-transaction", "事务前姓名") };
            });
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Assert.IsTrue(Transaction.HasTransaction);
                var statement = call.Arg<SqlStatement>();
                if (Conflict && (statement == OrganizationSql.UpdateUserUnitPrimary || statement == OrganizationSql.UpdateUserPositionPrimary)) return 0;
                Transaction.PendingWrites++;
                return 1;
            });
            _query.QuerySingleOrDefaultAsync<OrganizationUserUnitListRow>(OrganizationSql.FindUserUnitById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                ObserveLocalRead();
                return new OrganizationUserUnitListRow { Id = _assignment, UserId = _user, UnitId = _object,
                    Version = Transaction.PendingWrites > 0 ? 2 : 1, IsActive = operation != "disable" };
            });
            _query.QuerySingleOrDefaultAsync<OrganizationUserPositionListRow>(OrganizationSql.FindUserPositionById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                ObserveLocalRead();
                return new OrganizationUserPositionListRow { Id = _assignment, UserId = _user, PositionId = _object,
                    Version = Transaction.PendingWrites > 0 ? 2 : 1, IsActive = operation != "disable" };
            });
            _query.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(OrganizationSql.FindUnitById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new OrganizationUnitRecord(_object, _tenant, null, "unit", "机构", 0, true, DateTimeOffset.UtcNow, null, 1));
            _query.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(PositionSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new OrganizationPositionRecord(_object, _tenant, "position", "职位", null, null, null, null, null, null, 0, true, DateTimeOffset.UtcNow, null, 1));
            if (operation == "reactivate")
                _query.QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(OrganizationSql.FindUserUnitByTenantUserAndUnit, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                    .Returns(new OrganizationUserUnitRecord { Id = _assignment, UserId = _user, UnitId = _object, TenantId = _tenant, IsActive = false });
        }

        private void ObserveLocalRead()
        {
            if (Transaction.HasTransaction) LocalTransactionalReads++;
        }

        internal async Task<Outcome> RunAsync()
        {
            var tenant = Substitute.For<ICurrentTenant>();
            tenant.IsAvailable.Returns(true);
            tenant.Id.Returns(_tenant);
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_assignment);
            var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
            if (_kind == "unit")
            {
                var service = new TenantUserUnitManagementService(_query, Command, Transaction,
                    new TenantUserUnitQueryService(_query, _display),
                    new TenantUnitQueryService(_query, Substitute.For<IUserDataScopeResolver>(), Substitute.For<IDataScopeSqlFilterBuilder>(), options),
                    _members, tenant, clock, ids);
                var result = _operation switch
                {
                    "update" => await service.UpdateAsync(_assignment, new UpdateOrganizationUserUnitRequest(true, 1)),
                    "disable" => await service.DisableAsync(_assignment),
                    _ => await service.CreateAsync(new CreateOrganizationUserUnitRequest(_user, _object, true)),
                };
                return new(result.IsSuccess, result.Error, result.Value?.Username, result.Value?.Version ?? 0);
            }
            else
            {
                var service = new TenantUserPositionManagementService(_query, Command, Transaction,
                    new TenantUserPositionQueryService(_query, _display), new TenantPositionQueryService(_query, options), _members, tenant, clock, ids);
                var result = _operation switch
                {
                    "update" => await service.UpdateAsync(_assignment, new UpdateOrganizationUserPositionRequest(true, 1)),
                    "disable" => await service.DisableAsync(_assignment),
                    _ => await service.CreateAsync(new CreateOrganizationUserPositionRequest(_user, _object, true)),
                };
                return new(result.IsSuccess, result.Error, result.Value?.Username, result.Value?.Version ?? 0);
            }
        }
    }

    /// <summary>使用接口默认的失败 Result 回滚路径，记录真实事务委托的生存期。</summary>
    private sealed class BoundaryTransaction : ICommandTransaction, IDataTransactionState
    {
        public bool HasTransaction { get; private set; }
        internal int Executions { get; private set; }
        internal int Commits { get; private set; }
        internal int Rollbacks { get; private set; }
        internal int PendingWrites { get; set; }
        internal int CommittedWrites { get; private set; }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            Executions++;
            HasTransaction = true;
            try
            {
                var result = await action(cancellationToken);
                CommittedWrites += PendingWrites;
                Commits++;
                return result;
            }
            catch { Rollbacks++; throw; }
            finally { PendingWrites = 0; HasTransaction = false; }
        }
    }
}
