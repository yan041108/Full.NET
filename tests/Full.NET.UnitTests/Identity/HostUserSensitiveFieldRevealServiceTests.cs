using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserSensitiveFieldRevealServiceTests
{
    private static readonly Guid ActorUserId = Guid.CreateVersion7();
    private static readonly Guid TargetUserId = Guid.CreateVersion7();
    private static readonly Guid AuditId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Reveal_phone_number_returns_plaintext_and_writes_audit()
    {
        var query = new RevealQueryExecutor();
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            query,
            command,
            projectionFieldKeys: ["phone_number"],
            permissions: [IdentityUserManagementPermissions.RevealPhoneNumber]);

        var result = await service.RevealAsync(
            ActorUserId,
            TargetUserId,
            new RevealHostUserProfileFieldsRequest(["phone_number"]),
            "127.0.0.1",
            "unit-test",
            default);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("13800000000", result.Value!.Values["phone_number"]);
        await command.Received(1).ExecuteAsync(
            IdentitySql.InsertAuthAudit,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Reveal_without_permission_returns_forbidden()
    {
        var query = new RevealQueryExecutor();
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            query,
            command,
            projectionFieldKeys: ["phone_number"],
            permissions: []);

        var result = await service.RevealAsync(
            ActorUserId,
            TargetUserId,
            new RevealHostUserProfileFieldsRequest(["phone_number"]),
            null,
            null,
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task Reveal_unknown_user_returns_not_found()
    {
        var query = new RevealQueryExecutor { TargetExists = false };
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            query,
            command,
            projectionFieldKeys: ["phone_number"],
            permissions: [IdentityUserManagementPermissions.RevealPhoneNumber]);

        var result = await service.RevealAsync(
            ActorUserId,
            TargetUserId,
            new RevealHostUserProfileFieldsRequest(["phone_number"]),
            null,
            null,
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.NotFound, result.Error!.Type);
    }

    [TestMethod]
    public async Task Reveal_rejects_unsupported_field_keys()
    {
        var query = new RevealQueryExecutor();
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            query,
            command,
            projectionFieldKeys: ["phone_number", "email"],
            permissions:
            [
                IdentityUserManagementPermissions.RevealPhoneNumber,
                IdentityUserManagementPermissions.RevealIdCardNumber,
            ]);

        var result = await service.RevealAsync(
            ActorUserId,
            TargetUserId,
            new RevealHostUserProfileFieldsRequest(["email"]),
            null,
            null,
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Validation, result.Error!.Type);
    }

    [TestMethod]
    public async Task Reveal_requires_readable_field_grant()
    {
        var query = new RevealQueryExecutor();
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(
            query,
            command,
            projectionFieldKeys: ["remark"],
            permissions: [IdentityUserManagementPermissions.RevealPhoneNumber]);

        var result = await service.RevealAsync(
            ActorUserId,
            TargetUserId,
            new RevealHostUserProfileFieldsRequest(["phone_number"]),
            null,
            null,
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
    }

    private static HostUserSensitiveFieldRevealService CreateService(
        IQueryExecutor query,
        ICommandExecutor command,
        IReadOnlyList<string> projectionFieldKeys,
        IReadOnlyList<string> permissions)
    {
        var projectionResolver = Substitute.For<IUserFieldProjectionResolver>();
        projectionResolver.ResolveAsync(
                ActorUserId,
                null,
                FieldProjectionResourceKeys.HostUsers,
                Arg.Any<CancellationToken>())
            .Returns(new UserFieldProjection(
                FieldProjectionResourceKeys.HostUsers,
                projectionFieldKeys));

        var permissionSnapshots = Substitute.For<IPermissionSnapshotReader>();
        permissionSnapshots.ReadAsync(
                ActorUserId,
                "host",
                null,
                Arg.Any<CancellationToken>())
            .Returns(new PermissionSnapshot(permissions, false));

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(AuditId);

        return new HostUserSensitiveFieldRevealService(
            query,
            command,
            permissionSnapshots,
            projectionResolver,
            clock,
            idGenerator);
    }

    private sealed class RevealQueryExecutor : IQueryExecutor
    {
        public bool TargetExists { get; set; } = true;

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == IdentitySql.FindHostUserById)
            {
                return Task.FromResult((T?)(TargetExists
                    ? (object)new IdentityUserRecord { Id = TargetUserId }
                    : null));
            }

            if (statement.Name == "identity.list_host_user_profiles_by_ids.projected")
            {
                return Task.FromResult((T?)(object)new HostUserProfileRecord
                {
                    UserId = TargetUserId,
                    PhoneNumber = "13800000000",
                    IdCardNumber = "440101199001011234",
                });
            }

            return Task.FromResult<T?>(default);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>([]);
    }
}
