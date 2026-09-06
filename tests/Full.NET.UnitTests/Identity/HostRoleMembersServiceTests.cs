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
        fixture.Query.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityUserRecord(
                UserId,
                null,
                "host",
                "demo",
                "DEMO",
                "Demo User",
                "hash",
                true,
                0,
                null,
                Guid.CreateVersion7().ToString("N"),
                DateTimeOffset.UtcNow,
                null,
                1));

        var result = await fixture.Service.ReplaceAsync(
            RoleId,
            new ReplaceHostRoleMembersRequest([UserId], 3));

        Assert.IsTrue(result.IsSuccess);
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DeleteHostRoleMemberAssignments,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.EnsureUserRole,
            Arg.Is<IdentityUserRole>(item =>
                item != null
                && item.UserId == UserId
                && item.RoleId == RoleId),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(2).ExecuteAsync(
            IdentitySql.RotateSecurityStamp,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(2).ExecuteAsync(
            IdentitySql.RevokeAllUserSessions,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
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
