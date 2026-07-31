using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostRoleFieldGrants;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostRoleFieldGrantServiceTests
{
    private static readonly Guid RoleId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Unknown_or_mandatory_fields_are_rejected_before_writes()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ReplaceAsync(
            RoleId,
            ActorId,
            new ReplaceHostRoleFieldGrantsRequest(
                FieldProjectionResourceKeys.HostUsers,
                ["username", "database_column"],
                3));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.FieldProjectionInvalid, result.Error!.Code);
        await fixture.Command.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task Replacement_increments_role_version_and_writes_only_catalog_grants()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ReplaceAsync(
            RoleId,
            ActorId,
            new ReplaceHostRoleFieldGrantsRequest(
                FieldProjectionResourceKeys.HostUsers,
                ["preferred_locale", "failed_login_count"],
                3));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(4, result.Value!.Version);
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.UpdateHostRoleVersion,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(1).ExecuteAsync(
            IdentitySql.DeleteHostRoleFieldGrants,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
        await fixture.Command.Received(2).ExecuteAsync(
            IdentitySql.InsertHostRoleFieldGrant,
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Reading_unknown_persisted_field_fails_closed_without_echoing_it()
    {
        var fixture = new Fixture();
        fixture.Query.QueryAsync<string>(
                IdentitySql.GetHostRoleFieldGrants,
                Arg.Any<object>(),
                Arg.Any<CancellationToken>())
            .Returns(["preferred_locale", "PasswordHash"]);

        var result = await fixture.Service.GetAsync(
            RoleId,
            FieldProjectionResourceKeys.HostUsers);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.FieldProjectionInvalid, result.Error!.Code);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Query = Substitute.For<IQueryExecutor>();
            Query.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                    IdentitySql.FindHostRoleById,
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(new IdentityRoleRecord(
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
                    3));
            Command = Substitute.For<ICommandExecutor>();
            Command.ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-08-01T00:00:00Z"));
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            Service = new HostRoleFieldGrantService(
                Query,
                Command,
                new PassThroughTransaction(),
                FieldProjectionCatalog.CreateDefault(),
                clock,
                ids);
        }

        public ICommandExecutor Command { get; }

        public IQueryExecutor Query { get; }

        public HostRoleFieldGrantService Service { get; }
    }

    private sealed class PassThroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }
}
