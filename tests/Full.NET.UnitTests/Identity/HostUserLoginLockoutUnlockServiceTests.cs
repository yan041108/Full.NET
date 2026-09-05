using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserLoginLockoutUnlockServiceTests
{
    private static readonly Guid ActorUserId = Guid.CreateVersion7();
    private static readonly Guid TargetUserId = Guid.CreateVersion7();
    private static readonly Guid AuditId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now =
        new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task Unlock_clears_lockout_and_writes_audit()
    {
        var query = new UnlockQueryExecutor
        {
            Record = CreateLockedRecord(isActive: true),
        };
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(
                IdentitySql.ClearHostUserLoginLockout,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var service = CreateService(query, command);

        var result = await service.UnlockAsync(
            ActorUserId,
            TargetUserId,
            "127.0.0.1",
            "unit-test",
            default);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value!.IsActive);
        await command.Received(1).ExecuteAsync(
            IdentitySql.ClearHostUserLoginLockout,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(
            IdentitySql.InsertAuthAudit,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Unlock_rejects_disabled_user()
    {
        var query = new UnlockQueryExecutor
        {
            Record = CreateLockedRecord(isActive: false),
        };
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(query, command);

        var result = await service.UnlockAsync(
            ActorUserId,
            TargetUserId,
            null,
            null,
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.BusinessRule, result.Error!.Type);
        Assert.AreEqual(
            IdentityErrorCodes.UnlockInactiveUserRejected,
            result.Error.Code);
        await command.DidNotReceiveWithAnyArgs().ExecuteAsync(
            default!,
            default,
            default);
    }

    [TestMethod]
    public async Task Unlock_rejects_user_without_lockout_state()
    {
        var query = new UnlockQueryExecutor
        {
            Record = CreateLockedRecord(
                isActive: true,
                failedLoginCount: 0,
                clearLockoutEnd: true),
        };
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(query, command);

        var result = await service.UnlockAsync(
            ActorUserId,
            TargetUserId,
            null,
            null,
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.BusinessRule, result.Error!.Type);
        Assert.AreEqual(IdentityErrorCodes.LoginNotLocked, result.Error.Code);
    }

    [TestMethod]
    public async Task Unlock_unknown_user_returns_not_found()
    {
        var query = new UnlockQueryExecutor { Record = null };
        var command = Substitute.For<ICommandExecutor>();
        var service = CreateService(query, command);

        var result = await service.UnlockAsync(
            ActorUserId,
            TargetUserId,
            null,
            null,
            default);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.NotFound, result.Error!.Type);
    }

    [TestMethod]
    public void HasLoginLockoutState_detects_failed_count_or_lockout_end()
    {
        Assert.IsTrue(HostUserLoginLockoutUnlockService.HasLoginLockoutState(
            CreateLockedRecord(isActive: true, failedLoginCount: 1, lockoutEndUtc: null)));
        Assert.IsTrue(HostUserLoginLockoutUnlockService.HasLoginLockoutState(
            CreateLockedRecord(
                isActive: true,
                failedLoginCount: 0,
                lockoutEndUtc: Now.AddMinutes(5))));
        Assert.IsFalse(HostUserLoginLockoutUnlockService.HasLoginLockoutState(
            CreateLockedRecord(
                isActive: true,
                failedLoginCount: 0,
                clearLockoutEnd: true)));
    }

    private static HostUserLoginLockoutUnlockService CreateService(
        IQueryExecutor query,
        ICommandExecutor command)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(AuditId);

        return new HostUserLoginLockoutUnlockService(
            query,
            command,
            clock,
            idGenerator);
    }

    private static IdentityUserRecord CreateLockedRecord(
        bool isActive,
        int failedLoginCount = 3,
        DateTimeOffset? lockoutEndUtc = null,
        bool clearLockoutEnd = false) =>
        new()
        {
            Id = TargetUserId,
            Username = "locked-user",
            DisplayName = "Locked User",
            AccountType = "normal_user",
            IsActive = isActive,
            FailedLoginCount = failedLoginCount,
            LockoutEndUtc = clearLockoutEnd
                ? null
                : lockoutEndUtc ?? Now.AddMinutes(10),
            CreatedAtUtc = Now.AddDays(-1),
            UpdatedAtUtc = Now,
            Version = 1,
        };

    private sealed class UnlockQueryExecutor : IQueryExecutor
    {
        public IdentityUserRecord? Record { get; set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == IdentitySql.FindHostUserById)
            {
                return Task.FromResult((T?)(object?)Record);
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
