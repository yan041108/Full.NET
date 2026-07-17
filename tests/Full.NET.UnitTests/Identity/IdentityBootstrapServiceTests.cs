using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Identity;
using NSubstitute;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityBootstrapServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 17, 1, 2, 3, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("01981a08-0c40-7000-8000-000000000001");

    [TestMethod]
    public async Task Weak_password_is_rejected_before_database_access()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.BootstrapHostAdminAsync(
            new BootstrapHostAdminRequest("admin", "weak", "系统管理员"));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("identity.bootstrap.invalid-password", result.Error?.Code);
        await fixture.QueryExecutor.DidNotReceiveWithAnyArgs()
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(default!, default, default);
    }

    [TestMethod]
    public async Task Existing_admin_is_not_overwritten()
    {
        var fixture = new Fixture();
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityUserRecord(
                UserId,
                null,
                "host",
                "admin",
                "ADMIN",
                "系统管理员",
                "existing-hash",
                true,
                0,
                null,
                "stamp",
                Now,
                null,
                1));

        var result = await fixture.Service.BootstrapHostAdminAsync(
            new BootstrapHostAdminRequest(
                " Admin ",
                "FullNet!2026Secure",
                "系统管理员"));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value!.Created);
        Assert.AreEqual(UserId, result.Value.UserId);
        await fixture.CommandExecutor.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default, default);
    }

    [TestMethod]
    public async Task New_admin_is_hashed_and_inserted_once()
    {
        var fixture = new Fixture();
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((IdentityUserRecord?)null);
        fixture.CommandExecutor
            .ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await fixture.Service.BootstrapHostAdminAsync(
            new BootstrapHostAdminRequest(
                " Admin ",
                "FullNet!2026Secure",
                " 系统管理员 "));

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value!.Created);
        Assert.AreEqual(UserId, result.Value.UserId);
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.InsertUser,
            Arg.Is<IdentityUser>(user =>
                user != null
                && user.Id == UserId
                && user.Username == "Admin"
                && user.NormalizedUsername == "ADMIN"
                && user.DisplayName == "系统管理员"
                && user.PasswordHash != "FullNet!2026Secure"),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            CommandExecutor = Substitute.For<ICommandExecutor>();
            var idGenerator = Substitute.For<IIdGenerator>();
            idGenerator.NewId().Returns(UserId);
            Service = new IdentityBootstrapService(
                QueryExecutor,
                CommandExecutor,
                new PassthroughTransaction(),
                new PasswordHasher<IdentityUser>(),
                new FixedClock(),
                idGenerator);
        }

        public IQueryExecutor QueryExecutor { get; }

        public ICommandExecutor CommandExecutor { get; }

        public IdentityBootstrapService Service { get; }
    }

    private sealed class PassthroughTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) => action(cancellationToken);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
