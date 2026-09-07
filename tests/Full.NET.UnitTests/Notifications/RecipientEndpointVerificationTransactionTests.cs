using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Features.VerifyRecipientEndpoints;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>通过真实事务协调实现验证失败仍提交安全计数，避免错误响应无限回滚尝试次数。</summary>
[TestClass]
public sealed class RecipientEndpointVerificationTransactionTests
{
    /// <summary>验证码挑战先提交，外部发送不得占用本地事务；发送失败也保留冷却记录。</summary>
    /// <param name="providerSucceeds">模拟提供程序是否明确受理。</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Sending_occurs_after_challenge_commit(bool providerSucceeds)
    {
        var coordinator = new AttemptCoordinator();
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var protector = new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider());
        var userId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        queries.QuerySingleOrDefaultAsync<NotificationRecipientEndpointProtectedRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationRecipientEndpointProtectedRecord(
                endpointId, userId, Guid.NewGuid(), "email", protector.Protect("recipient@example.test"), "pending"));
        var sender = new RecordingSender(coordinator, providerSucceeds);
        var service = new RecipientEndpointVerificationService(
            queries, commands, new DapperCommandTransaction(coordinator), tenant,
            protector, sender, sender, clock, Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        var result = await service.SendCodeAsync(userId, endpointId);

        Assert.AreEqual(providerSucceeds, result.IsSuccess);
        Assert.IsFalse(sender.SentInsideTransaction, "SMTP/短信发送不能持有数据库事务。");
        Assert.AreEqual(1, sender.CommitsBeforeSend, "外发前必须先提交挑战和冷却事实。");
        Assert.AreEqual(1, coordinator.Commits);
    }

    /// <summary>连续错误验证码达到上限后，五次安全状态均已提交。</summary>
    [TestMethod]
    public async Task Wrong_codes_commit_attempts_until_exhausted()
    {
        var userId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var coordinator = new AttemptCoordinator();
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        queries.QuerySingleOrDefaultAsync<NotificationRecipientEndpointProtectedRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationRecipientEndpointProtectedRecord(
                endpointId, userId, Guid.NewGuid(), "email", "unused", "pending"));
        queries.QuerySingleOrDefaultAsync<NotificationRecipientEndpointChallengeRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new NotificationRecipientEndpointChallengeRecord(
                challengeId, endpointId, "host", userId,
                RecipientEndpointVerificationCodeHasher.Hash(challengeId, "123456"),
                coordinator.Attempts, 5, now.AddMinutes(15), null, now));
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<SqlStatement>(0) == NotificationRecipientEndpointChallengeSql.IncrementAttempt)
                {
                    coordinator.Attempts++;
                }

                return 1;
            });
        var sender = new UnusedSender();
        var service = new RecipientEndpointVerificationService(
            queries, commands, new DapperCommandTransaction(coordinator), tenant,
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            sender, sender, clock, Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var result = await service.VerifyCodeAsync(userId, endpointId, new VerifyRecipientEndpointCodeRequest("654321"));
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(attempt, coordinator.Attempts, "错误响应不能撤销已经发生的猜测次数。");
            Assert.AreEqual(attempt, coordinator.Commits);
            if (attempt == 5)
            {
                Assert.AreEqual(NotificationsErrorCodes.RecipientEndpointVerificationAttemptsExhausted, result.Error!.Code);
            }
        }
    }

    /// <summary>已由其他请求消费的挑战不能再次把端点升级为已验证。</summary>
    [TestMethod]
    public async Task Consumed_challenge_cannot_verify_endpoint()
    {
        var userId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var challengeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(true);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        queries.QuerySingleOrDefaultAsync<NotificationRecipientEndpointProtectedRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationRecipientEndpointProtectedRecord(endpointId, userId, Guid.NewGuid(), "email", "unused", "pending"));
        queries.QuerySingleOrDefaultAsync<NotificationRecipientEndpointChallengeRecord>(
            Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationRecipientEndpointChallengeRecord(challengeId, endpointId, "host", userId,
                RecipientEndpointVerificationCodeHasher.Hash(challengeId, "123456"), 0, 5, now.AddMinutes(15), null, now));
        var sender = new UnusedSender();
        var service = new RecipientEndpointVerificationService(queries, commands,
            new DapperCommandTransaction(new AttemptCoordinator()), tenant,
            new NotificationRecipientEndpointProtector(new EphemeralDataProtectionProvider()),
            sender, sender, clock, Substitute.For<IIdGenerator>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        var result = await service.VerifyCodeAsync(userId, endpointId, new VerifyRecipientEndpointCodeRequest("123456"));

        Assert.IsFalse(result.IsSuccess);
        await commands.DidNotReceive().ExecuteAsync(NotificationRecipientEndpointSql.MarkVerified,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>在事务边界提交或回滚测试内存中的计数，复现持久化状态的提交语义。</summary>
    private sealed class AttemptCoordinator : IDbTransactionCoordinator
    {
        private int _baseline;
        public bool HasTransaction { get; private set; }
        public int Attempts { get; set; }
        public int Commits { get; private set; }

        /// <summary>保存事务开始前的安全计数。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task BeginAsync(CancellationToken cancellationToken)
        {
            _baseline = Attempts;
            HasTransaction = true;
            return Task.CompletedTask;
        }

        /// <summary>保留计数变更并结束事务。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Commits++;
            HasTransaction = false;
            return Task.CompletedTask;
        }

        /// <summary>撤销尚未提交的计数变更。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            Attempts = _baseline;
            HasTransaction = false;
            return Task.CompletedTask;
        }
    }

    /// <summary>验证流程不应触发任何外部发送。</summary>
    private sealed class UnusedSender : IRecipientEndpointVerificationMailSender, IRecipientEndpointVerificationSmsSender
    {
        /// <summary>阻止验证测试意外进入发送链路。</summary>
        /// <param name="providerProfileVersionId">提供程序配置版本。</param>
        /// <param name="recipientEmail">待验证端点。</param>
        /// <param name="code">验证码。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task<Result<bool>> SendAsync(Guid providerProfileVersionId, string recipientEmail, string code, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("校验验证码不得外发消息。");
    }

    /// <summary>记录验证码外发时的事务边界，模拟明确成功或失败的提供程序。</summary>
    /// <param name="coordinator">当前服务使用的事务观察器。</param>
    /// <param name="succeeds">提供程序是否受理。</param>
    private sealed class RecordingSender(AttemptCoordinator coordinator, bool succeeds)
        : IRecipientEndpointVerificationMailSender, IRecipientEndpointVerificationSmsSender
    {
        public bool SentInsideTransaction { get; private set; }
        public int CommitsBeforeSend { get; private set; }

        /// <summary>记录调用边界并返回模拟受理结果。</summary>
        /// <param name="providerProfileVersionId">提供程序配置版本。</param>
        /// <param name="recipientEmail">待验证端点。</param>
        /// <param name="code">验证码。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task<Result<bool>> SendAsync(Guid providerProfileVersionId, string recipientEmail, string code, CancellationToken cancellationToken)
        {
            SentInsideTransaction = coordinator.HasTransaction;
            CommitsBeforeSend = coordinator.Commits;
            return Task.FromResult(succeeds
                ? Result<bool>.Success(true)
                : Result<bool>.Failure(new Error("test.provider_failed", "提供程序明确拒绝受理。", ErrorType.Unexpected)));
        }
    }
}
