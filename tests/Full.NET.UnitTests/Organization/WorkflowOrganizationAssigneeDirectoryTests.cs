using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Organization.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.TenantUnits;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

/// <summary>组织来源只能读取可信租户，并区分无效来源、空成员和真实组织链。</summary>
[TestClass]
public sealed class WorkflowOrganizationAssigneeDirectoryTests
{
    [TestMethod]
    public async Task Direct_manager_distinguishes_no_mapping_explicit_clear_and_corrupt_self_mapping()
    {
        var f = Create(); var employee = Guid.CreateVersion7();
        var absent = await f.Directory.ResolveDirectManagerAsync(employee); Assert.IsTrue(absent.SourceIsValid); Assert.IsNull(absent.ManagerUserId);
        var id = Guid.CreateVersion7(); var row = new OrganizationReportingLineRecord(id, employee, null, 3, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        f.Query.QuerySingleOrDefaultAsync<OrganizationReportingLineRecord>(OrganizationReportingLineSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var cleared = await f.Directory.ResolveDirectManagerAsync(employee); Assert.IsTrue(cleared.SourceIsValid); Assert.AreEqual(3L, cleared.Revision); Assert.IsNull(cleared.ManagerUserId);
        f.Query.QuerySingleOrDefaultAsync<OrganizationReportingLineRecord>(OrganizationReportingLineSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row with { ManagerUserId = employee });
        Assert.IsFalse((await f.Directory.ResolveDirectManagerAsync(employee)).SourceIsValid);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Direct_membership_checks_bind_one_actual_user_without_relisting_rosters(bool position)
    {
        var f = Create(); var actor = Guid.CreateVersion7(); var source = Guid.CreateVersion7();
        var statement = position ? WorkflowOrganizationAssigneeSql.IsPositionMember : WorkflowOrganizationAssigneeSql.IsUnitMember;
        f.Query.QuerySingleOrDefaultAsync<int>(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var actual = position ? await f.Directory.IsActiveMemberOfAnyPositionAsync(actor, [source]) : await f.Directory.IsActiveMemberOfAnyUnitAsync(actor, [source]);
        Assert.IsTrue(actual);
        await f.Query.Received(1).QuerySingleOrDefaultAsync<int>(statement,
            Arg.Is<object?>(parameters => HasValue(parameters, "UserId", actor)), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Direct_membership_checks_reject_ambient_transactions(bool position)
    {
        var f = Create(inTransaction: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => position
            ? f.Directory.IsActiveMemberOfAnyPositionAsync(Guid.CreateVersion7(), [Guid.CreateVersion7()])
            : f.Directory.IsActiveMemberOfAnyUnitAsync(Guid.CreateVersion7(), [Guid.CreateVersion7()]));
        Assert.AreEqual(0, f.Query.ReceivedCalls().Count());
    }
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task Directory_rejects_host_or_ambient_transaction_before_any_query(bool host, bool inTransaction)
    {
        var f = Create(host, inTransaction);
        if (inTransaction)
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Directory.FindActivePositionsAsync([Guid.CreateVersion7()]));
        else
            await Assert.ThrowsAsync<TenantContextMissingException>(() => f.Directory.FindActivePositionsAsync([Guid.CreateVersion7()]));
        Assert.AreEqual(0, f.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(22)]
    public async Task Member_limit_cannot_silently_expand_or_return_an_empty_window(int maximum)
    {
        var f = Create();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Directory.FindActivePositionMemberUserIdsAsync([Guid.CreateVersion7()], maximum));
        Assert.AreEqual(0, f.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Position_members_are_batched_and_preserve_empty_sources_without_inventing_members()
    {
        var f = Create();
        var filled = Guid.CreateVersion7(); var empty = Guid.CreateVersion7(); var invalid = Guid.CreateVersion7(); var user = Guid.CreateVersion7();
        f.Query.QueryAsync<WorkflowOrganizationPositionListRow>(WorkflowOrganizationAssigneeSql.FindActivePositionsByIds,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] {
                new WorkflowOrganizationPositionListRow(filled, "filled", "Filled"), new WorkflowOrganizationPositionListRow(empty, "empty", "Empty") });
        f.Query.QueryAsync<WorkflowOrganizationAssigneeMemberRow>(WorkflowOrganizationAssigneeSql.FindPositionMembers,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { new WorkflowOrganizationAssigneeMemberRow(filled, user) });
        var result = await f.Directory.FindActivePositionMemberUserIdsAsync([filled, empty, invalid], 21);
        Assert.AreEqual(2, result.Count); CollectionAssert.AreEqual(new[] { user }, result[filled].ToArray());
        Assert.AreEqual(0, result[empty].Count); Assert.IsFalse(result.ContainsKey(invalid));
        Assert.AreEqual(2, f.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false, false, 2)]
    [DataRow(true, false, 0)]
    [DataRow(false, true, 0)]
    public async Task Leader_chain_preserves_real_levels_and_rejects_cycles_or_missing_leaders(bool cycle, bool missingLeader, int expected)
    {
        var f = Create(); var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7();
        var firstLeader = Guid.CreateVersion7(); var secondLeader = Guid.CreateVersion7();
        f.Query.QuerySingleOrDefaultAsync<Guid?>(WorkflowOrganizationAssigneeSql.FindInitiatorPrimaryUnitId,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(first);
        f.Query.QuerySingleOrDefaultAsync<OrganizationLeaderSourceUnitRecord>(OrganizationUnitLeadershipSql.ReadSourceUnit,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
                HasValue(call.ArgAt<object?>(1), "UnitId", first) ? new(first, second, firstLeader) : new(second, cycle ? first : null, missingLeader ? null : secondLeader));
        var resolution = await f.Directory.ResolveLeaderSourceAsync(Guid.CreateVersion7(), new(WorkflowOrganizationLeaderSourceKind.InitiatorLeaderChain, Depth: 20));
        Assert.AreEqual(expected > 0, resolution.SourceIsValid); var result = resolution.Leaders;
        Assert.AreEqual(expected, result.Count);
        if (expected > 0)
        {
            Assert.AreEqual(first, result[0].UnitId); Assert.AreEqual(firstLeader, result[0].UserId); Assert.AreEqual(1, result[0].Level);
            Assert.AreEqual(second, result[1].UnitId); Assert.AreEqual(secondLeader, result[1].UserId); Assert.AreEqual(2, result[1].Level);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Explicit_unit_without_leader_is_valid_empty_but_missing_unit_is_invalid(bool exists)
    {
        var f = Create(); var unit = Guid.CreateVersion7();
        f.Query.QuerySingleOrDefaultAsync<OrganizationLeaderSourceUnitRecord>(OrganizationUnitLeadershipSql.ReadSourceUnit,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(exists ? new OrganizationLeaderSourceUnitRecord(unit, null, null) : (OrganizationLeaderSourceUnitRecord?)null);
        var result = await f.Directory.ResolveLeaderSourceAsync(Guid.Empty, new(WorkflowOrganizationLeaderSourceKind.Unit, unit));
        Assert.AreEqual(exists, result.SourceIsValid); Assert.HasCount(0, result.Leaders);
        Assert.AreEqual(1, f.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_primary_unit_is_valid_empty_and_does_not_guess_from_membership()
    {
        var f = Create(); var result = await f.Directory.ResolveLeaderSourceAsync(Guid.CreateVersion7(), new(WorkflowOrganizationLeaderSourceKind.InitiatorPrimaryUnit));
        Assert.IsTrue(result.SourceIsValid); Assert.HasCount(0, result.Leaders); Assert.AreEqual(1, f.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Leadership_rejects_ambient_transaction_before_reading_authority()
    {
        var f = Create(inTransaction: true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Directory.ResolveLeaderSourceAsync(Guid.Empty, new(WorkflowOrganizationLeaderSourceKind.Unit, Guid.CreateVersion7())));
        Assert.AreEqual(0, f.Query.ReceivedCalls().Count());
    }

    private static bool HasValue(object? parameters, string name, object expected) => parameters is IReadOnlyDictionary<string, object?> values &&
        values.TryGetValue(name, out var actual) && Equals(actual, expected);

    private static Fixture Create(bool host = false, bool inTransaction = false)
    {
        var query = Substitute.For<IQueryExecutor>(); var tenant = Substitute.For<ICurrentTenant>();
        tenant.Id.Returns(Guid.CreateVersion7()); tenant.IsHost.Returns(host); tenant.IsAvailable.Returns(true);
        var transaction = Substitute.For<IDataTransactionState>(); transaction.HasTransaction.Returns(inTransaction);
        return new(new WorkflowOrganizationAssigneeDirectory(query, Options.Create(new DatabaseOptions { Provider=DatabaseProvider.SqlServer }), tenant, transaction), query);
    }

    private sealed record Fixture(WorkflowOrganizationAssigneeDirectory Directory, IQueryExecutor Query);
}
