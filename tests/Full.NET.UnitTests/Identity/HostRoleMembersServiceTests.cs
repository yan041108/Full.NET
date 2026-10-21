using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>单元测试（Identity）：Host Role Members Service。</summary>
[TestClass]
public sealed class HostRoleMembersServiceTests
{
    private static readonly Guid RoleId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Replace_rejects_system_role()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(fixture.DefaultRole with { IsSystem = true });

        var result = await fixture.Service.ReplaceAsync(
            RoleId,
            new ReplaceHostRoleMembersRequest([UserId], 3));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.RoleSystemLocked, result.Error!.Code);
        await fixture.Command.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Replace_rotates_security_stamps_and_revokes_sessions_for_changed_users()
    {
        var fixture = new Fixture();
        var previousUserId = Guid.CreateVersion7();
        fixture.Query.QueryAsync<Guid>(
                IdentitySql.ListHostRoleMemberUserIds,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns([previousUserId]);

        var result = await fixture.Service.ReplaceAsync(
            RoleId,
            new ReplaceHostRoleMembersRequest([UserId], 3));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DeleteHostRoleMemberAssignments,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(HostRoleMembersSql.InsertMembersSqlServer,
            Arg.Any<object>(), Arg.Any<CancellationToken>());
        var revoke = fixture.Command.ReceivedCalls().Single(call =>
            ReferenceEquals(call.GetArguments()[0], HostRoleMembersSql.RevokeSessionsSqlServer)).GetArguments()[1];
        var values = (IReadOnlyDictionary<string, object?>)revoke!;
        using var stamps = JsonDocument.Parse((string)values["UserStampsJson"]!);
        var rows = stamps.RootElement.EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(new[] { UserId, previousUserId }, rows.Select(row => row.GetProperty("userId").GetGuid()).ToArray());
        Assert.AreEqual(2, rows.Select(row => row.GetProperty("securityStamp").GetString()).Distinct().Count());
        await fixture.Command.DidNotReceive().ExecuteAsync(IdentitySql.RotateSecurityStamp, Arg.Any<object>(), Arg.Any<CancellationToken>());

    }

    [TestMethod]
    public async Task List_rejects_super_administrator_role()
    {
        var fixture = new Fixture();
        fixture.Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(fixture.DefaultRole with { IsSuperAdministrator = true });

        var result = await fixture.Service.ListAsync(RoleId, 1, 20);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.RoleSystemLocked, result.Error!.Code);
    }

    [TestMethod]
    public async Task Thousand_members_use_bounded_database_calls()
    {
        var fixture = new Fixture();
        var ids = Enumerable.Range(0, 1000).Select(_ => Guid.CreateVersion7()).ToArray();
        var databaseOrder = ids.Reverse().ToArray();
        fixture.Query.QueryAsync<HostRoleMemberUserState>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(databaseOrder.Select(id => new HostRoleMemberUserState(id, true)).ToArray());
        var result = await fixture.Service.ReplaceAsync(RoleId, new ReplaceHostRoleMembersRequest(ids, 3));
        Assert.IsTrue(result.IsSuccess);
        Assert.IsLessThanOrEqualTo(10, fixture.Query.ReceivedCalls().Count() + fixture.Command.ReceivedCalls().Count());
        var revoke = fixture.Command.ReceivedCalls().Single(call =>
            ReferenceEquals(call.GetArguments()[0], HostRoleMembersSql.RevokeSessionsSqlServer)).GetArguments()[1];
        using var stamps = JsonDocument.Parse((string)((IReadOnlyDictionary<string, object?>)revoke!)["UserStampsJson"]!);
        CollectionAssert.AreEqual(databaseOrder, stamps.RootElement.EnumerateArray()
            .Select(row => row.GetProperty("userId").GetGuid()).ToArray(), "分块必须沿用数据库返回的锁定顺序。");
    }

    [TestMethod]
    public async Task Missing_member_stops_before_assignment_or_revocation()
    {
        var fixture = new Fixture();
        fixture.Query.QueryAsync<HostRoleMemberUserState>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<HostRoleMemberUserState>());
        var result = await fixture.Service.ReplaceAsync(RoleId, new ReplaceHostRoleMembersRequest([UserId], 3));
        Assert.IsFalse(result.IsSuccess);
        await fixture.Command.DidNotReceive().ExecuteAsync(IdentitySql.DeleteHostRoleMemberAssignments, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Candidate_save_preserves_members_not_shown_in_loaded_pages()
    {
        var fixture = new Fixture(); var unseen = Guid.CreateVersion7(); var removed = Guid.CreateVersion7();
        fixture.Query.QueryAsync<Guid>(IdentitySql.ListHostRoleMemberUserIds, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns([unseen, removed]);
        var result = await fixture.Service.ReplaceAsync(RoleId,
            new ReplaceHostRoleMembersRequest([UserId], 3) { CandidateUserIds = [UserId, removed] });
        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEquivalent(new[] { unseen, UserId }, result.Value!.UserIds.ToArray());
        var insert = fixture.Command.ReceivedCalls().Single(call => ReferenceEquals(call.GetArguments()[0], HostRoleMembersSql.InsertMembersSqlServer));
        var parameters = (IReadOnlyDictionary<string,object?>)insert.GetArguments()[1]!;
        CollectionAssert.AreEquivalent(new[] { unseen, UserId }, JsonSerializer.Deserialize<Guid[]>((string)parameters["UserIdsJson"]!)!);
    }

    [TestMethod]
    public async Task List_decodes_empty_candidate_marker_after_original_budget_check()
    {
        var fixture = new Fixture();
        var empty = await fixture.Service.ListAsync(RoleId, 1, 20, userIds: [Guid.Empty]);
        Assert.IsTrue(empty.IsSuccess);
        Assert.AreEqual(0L, empty.Value!.Total);
        Assert.IsEmpty(empty.Value.Items);
        await fixture.Query.DidNotReceive().QuerySingleOrDefaultAsync<long>(
            Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
        await fixture.Query.DidNotReceive().QueryAsync<HostRoleMemberRow>(
            Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>());

        var budget = new Fixture();
        var rejected = await budget.Service.ListAsync(RoleId, 1, 20,
            userIds: Enumerable.Repeat(Guid.Empty, 101).ToArray());
        Assert.IsFalse(rejected.IsSuccess);
        Assert.AreEqual(ValidationErrorCodes.Failed, rejected.Error!.Code);
        await budget.Query.DidNotReceive().QuerySingleOrDefaultAsync<IdentityRoleRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Query = Substitute.For<IQueryExecutor>();
            DefaultRole = new IdentityRoleRecord(
                RoleId,
                null,
                "host",
                "auditor",
                "Auditor",
                false,
                true,
                false,
                RoleDataScopeKinds.All,
                DateTimeOffset.UtcNow,
                null,
                3);
            Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                    IdentitySql.FindHostRoleById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(DefaultRole);
            Query.QuerySingleOrDefaultAsync<long>(
                    IdentitySql.CountHostRoleMembers,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(0);
            Query.QueryAsync<HostRoleMemberRow>(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(Array.Empty<HostRoleMemberRow>());
            Query.QueryAsync<Guid>(
                    IdentitySql.ListHostRoleMemberUserIds,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(Array.Empty<Guid>());
            Query.QueryAsync<HostRoleMemberUserState>(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(call => JsonSerializer.Deserialize<Guid[]>((string)((IReadOnlyDictionary<string, object?>)call.Arg<object>()!)["UserIdsJson"]!)!
                    .Select(id => new HostRoleMemberUserState(id, true)).ToArray());
            Command = Substitute.For<ICommandExecutor>();
            Command.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-09-06T00:00:00Z"));
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            Service = new HostRoleMembersService(
                Query,
                Command,
                new PassThroughTransaction(),
                clock,
                ids,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                    ConnectionString = "Server=.;Database=test;",
                }));
        }

        public IdentityRoleRecord DefaultRole { get; }

        public ICommandExecutor Command { get; }

        public IQueryExecutor Query { get; }

        public HostRoleMembersService Service { get; }
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
