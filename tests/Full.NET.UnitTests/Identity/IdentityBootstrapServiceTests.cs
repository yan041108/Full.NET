using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;
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
    private static readonly Guid RoleId =
        Guid.Parse("01981a08-0c40-7000-8000-000000000002");

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
    public async Task Empty_password_returns_five_ordered_policy_violations()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.BootstrapHostAdminAsync(
            new BootstrapHostAdminRequest("admin", string.Empty, "系统管理员"));

        Assert.IsFalse(result.IsSuccess);
        var error = result.Error!;
        var messages = error.ValidationErrors!["Password"];
        var violations = error.ValidationViolations!;
        var policyViolations = IdentityPasswordPolicy.Validate(string.Empty);
        Assert.HasCount(5, messages);
        Assert.HasCount(5, violations);
        CollectionAssert.AreEqual(
            policyViolations.Select(violation => violation.DefaultMessage).ToArray(),
            messages);
        CollectionAssert.AreEqual(
            new[]
            {
                IdentityErrorCodes.PasswordMinimumLength,
                IdentityErrorCodes.PasswordUppercaseRequired,
                IdentityErrorCodes.PasswordLowercaseRequired,
                IdentityErrorCodes.PasswordDigitRequired,
                IdentityErrorCodes.PasswordNonAlphanumericRequired,
            },
            violations.Select(violation => violation.Code).ToArray());
        Assert.IsTrue(violations.All(violation => violation.Field == "Password"));
        CollectionAssert.AreEqual(
            new[] { "MinLength" },
            violations[0].Arguments.Keys.ToArray());
        Assert.AreEqual(IdentityPasswordPolicy.MinimumLength,
            violations[0].Arguments["MinLength"]);
        Assert.IsTrue(violations.Skip(1).All(violation => violation.Arguments.Count == 0));
        for (var index = 0; index < violations.Count; index++)
        {
            Assert.AreEqual(policyViolations[index].Code, violations[index].Code);
            CollectionAssert.AreEquivalent(
                policyViolations[index].Arguments.ToArray(),
                violations[index].Arguments.ToArray());
        }
    }

    [TestMethod]
    public async Task Existing_admin_is_not_overwritten_and_authorization_is_synchronized()
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
        await fixture.CommandExecutor.DidNotReceive().ExecuteAsync(
            IdentitySql.InsertUser,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.InsertRole,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await fixture.CommandExecutor.Received(4).ExecuteAsync(
            IdentitySql.EnsureRolePermission,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await fixture.CommandExecutor.Received(1).ExecuteAsync(
            IdentitySql.EnsureUserRole,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
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

    [TestMethod]
    public async Task Existing_role_only_receives_missing_known_permissions()
    {
        var fixture = new Fixture();
        fixture.SetExistingUser();
        fixture.QueryExecutor
            .QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindRoleByScopeAndCode,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new IdentityRoleRecord(
                RoleId,
                null,
                "host",
                "host-administrator",
                "宿主管理员",
                true,
                true,
                Now,
                null,
                1));
        fixture.QueryExecutor
            .QueryAsync<string>(
                IdentitySql.GetRolePermissionCodes,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(["platform.dashboard.read", "custom.retained"]);

        var result = await fixture.Service.BootstrapHostAdminAsync(
            new BootstrapHostAdminRequest(
                "admin",
                "FullNet!2026Secure",
                "系统管理员"));

        Assert.IsTrue(result.IsSuccess);
        await fixture.CommandExecutor.DidNotReceive().ExecuteAsync(
            IdentitySql.InsertRole,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await fixture.CommandExecutor.Received(3).ExecuteAsync(
            IdentitySql.EnsureRolePermission,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            QueryExecutor = Substitute.For<IQueryExecutor>();
            CommandExecutor = Substitute.For<ICommandExecutor>();
            QueryExecutor
                .QueryAsync<string>(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns([]);
            CommandExecutor
                .ExecuteAsync(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            var idGenerator = Substitute.For<IIdGenerator>();
            idGenerator.NewId().Returns(UserId, RoleId);
            var catalog = AuthorizationCatalog.Create(
                [new IdentityAuthorizationContributor(), new TenancyAuthorizationContributor()]);
            Service = new IdentityBootstrapService(
                QueryExecutor,
                CommandExecutor,
                new PassthroughTransaction(),
                new PasswordHasher<IdentityUser>(),
                new FixedClock(),
                idGenerator,
                catalog);
        }

        public IQueryExecutor QueryExecutor { get; }

        public ICommandExecutor CommandExecutor { get; }

        public IdentityBootstrapService Service { get; }

        public void SetExistingUser()
        {
            QueryExecutor
                .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindUserByScopeAndUsername,
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
        }
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
