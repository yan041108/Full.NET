using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class AccountChallengeConcurrencyAssertions
{
    // 纯挑战夹具提供稳定恢复账号；注册和邀请用途仍使用原摘要。
    private static readonly Guid RecoveryUserId = Guid.Parse("018f5f40-0000-7000-8000-000000000123");

    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var delivery = new CapturingDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services => services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery));
        await scopedFactory.InitializeAsync();
        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountChallengeService>();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        foreach (var purpose in new[]
        {
            IdentityAccountChallengePurpose.RegistrationEmailVerification,
            IdentityAccountChallengePurpose.PasswordRecovery,
            IdentityAccountChallengePurpose.InvitationEmailVerification,
        })
        {
            var intent = await CreateAsync();
            var failed = await RaceAsync(scopedFactory, intent, "incorrect-code", 8);
            Assert.IsTrue(failed.All(result => !result.IsSuccess));
            var exhausted = await ReadAsync(intent);
            Assert.AreEqual(exhausted.MaxAttempts, exhausted.AttemptCount,
                "同一版本快照上的并发错误凭据必须逐次计数，并在上限处停止。");
            Assert.AreEqual(1 + exhausted.MaxAttempts, exhausted.Version);
            Assert.IsNull(exhausted.ConsumedAtUtc);
            var blocked = await service.ConsumeAsync(intent.ChallengeId, purpose, intent.NormalizedEmail, intent.Credential, recoveryUserId: RecoveryUserId);
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeAttemptsExceeded, blocked.Error?.Code);
            var overflow = await service.ConsumeAsync(intent.ChallengeId, purpose, intent.NormalizedEmail, "incorrect-code", recoveryUserId: RecoveryUserId);
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeAttemptsExceeded, overflow.Error?.Code);
            Assert.AreEqual(exhausted.AttemptCount, (await ReadAsync(intent)).AttemptCount);

            intent = await CreateAsync();
            var accepted = await RaceAsync(scopedFactory, intent, intent.Credential, 2);
            Assert.AreEqual(1, accepted.Count(result => result.IsSuccess));
            var consumed = await ReadAsync(intent);
            Assert.IsNotNull(consumed.ConsumedAtUtc);
            Assert.AreEqual(0, consumed.AttemptCount);
            Assert.AreEqual(2, consumed.Version);
            Assert.IsFalse((await service.ConsumeAsync(intent.ChallengeId, purpose, intent.NormalizedEmail, intent.Credential, recoveryUserId: RecoveryUserId)).IsSuccess);

            // 正确请求已读到活跃快照，但写入前其他请求耗尽次数，旧快照也不能绕过上限。
            intent = await CreateAsync();
            await VerifyDelayedAsync(intent, intent.Credential, async () =>
            {
                await RaceAsync(scopedFactory, intent, "incorrect-code", 5);
            });
            exhausted = await ReadAsync(intent);
            Assert.AreEqual(exhausted.MaxAttempts, exhausted.AttemptCount);
            Assert.IsNull(exhausted.ConsumedAtUtc);

            // 错误请求读完后另一个请求已消费，迟到的计数不能再修改已消费记录。
            intent = await CreateAsync();
            await VerifyDelayedAsync(intent, "incorrect-code", async () =>
            {
                Assert.IsTrue((await service.ConsumeAsync(intent.ChallengeId, purpose, intent.NormalizedEmail, intent.Credential, recoveryUserId: RecoveryUserId)).IsSuccess);
            });
            consumed = await ReadAsync(intent);
            Assert.IsNotNull(consumed.ConsumedAtUtc);
            Assert.AreEqual(0, consumed.AttemptCount);
            Assert.AreEqual(2, consumed.Version);

            async Task<IdentityChallengeDeliveryIntent> CreateAsync()
            {
                var created = await service.CreateAndDeliverAsync(purpose, $"race-{Guid.NewGuid():N}@example.test", recoveryUserId: RecoveryUserId);
                Assert.IsTrue(created.IsSuccess);
                Assert.IsNotNull(delivery.LastIntent);
                Assert.AreEqual(created.Value!.ChallengeId, delivery.LastIntent.ChallengeId);
                return delivery.LastIntent;
            }

            async Task<AccountChallengeRecord> ReadAsync(IdentityChallengeDeliveryIntent current)
            {
                var record = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                    IdentitySqlParameters.Create(("ChallengeId", current.ChallengeId)));
                Assert.IsNotNull(record);
                return record;
            }

            async Task VerifyDelayedAsync(IdentityChallengeDeliveryIntent current, string credential, Func<Task> mutation)
            {
                await using var delayedScope = scopedFactory.Services.CreateAsyncScope();
                var gate = new ReadGate(1);
                var pending = CreateConsumer(delayedScope.ServiceProvider, gate)
                    .ConsumeAsync(current.ChallengeId, purpose, current.NormalizedEmail, credential, recoveryUserId: RecoveryUserId);
                Result<bool>? result = null;
                try
                {
                    await gate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    await mutation();
                }
                finally
                {
                    gate.Release.TrySetResult(true);
                    result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
                }
                Assert.IsFalse(result.IsSuccess);
            }
        }
    }

    private static async Task<Result<bool>[]> RaceAsync(FullNetApiFactory factory,
        IdentityChallengeDeliveryIntent intent, string credential, int count)
    {
        var scopes = Enumerable.Range(0, count).Select(_ => factory.Services.CreateAsyncScope()).ToArray();
        var gate = new ReadGate(count);
        // 每个请求都使用独立连接；等所有真实数据库读取完成后才允许写入，避免串行执行掩盖丢失更新。
        var pending = Task.WhenAll(scopes.Select(scope => CreateConsumer(scope.ServiceProvider, gate)
            .ConsumeAsync(intent.ChallengeId, intent.Purpose, intent.NormalizedEmail, credential, recoveryUserId: RecoveryUserId)));
        try
        {
            await gate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            gate.Release.TrySetResult(true);
            return await pending.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            gate.Release.TrySetResult(true);
            try { await pending.WaitAsync(TimeSpan.FromSeconds(30)); }
            finally { foreach (var scope in scopes) await scope.DisposeAsync(); }
        }
    }

    private static AccountChallengeService CreateConsumer(IServiceProvider services, ReadGate gate) => new(
        new PausedQueryExecutor(services.GetRequiredService<IQueryExecutor>(), gate),
        services.GetRequiredService<ICommandExecutor>(),
        services.GetRequiredService<ICommandTransaction>(),
        services.GetRequiredService<IIdentityChallengeDeliveryPort>(),
        services.GetRequiredService<IClock>(),
        services.GetRequiredService<IIdGenerator>());

    private sealed class PausedQueryExecutor(IQueryExecutor inner, ReadGate gate) : IQueryExecutor
    {
        public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
            await gate.ArriveAsync(cancellationToken);
            return result;
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) => inner.QueryAsync<T>(statement, parameters, cancellationToken);
    }

    private sealed class ReadGate(int expected)
    {
        private int arrived;
        public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrived) == expected) Ready.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CapturingDeliveryPort : IIdentityChallengeDeliveryPort
    {
        public IdentityChallengeDeliveryIntent? LastIntent { get; private set; }
        public Task<Result<bool>> SendAsync(IdentityChallengeDeliveryIntent intent, CancellationToken cancellationToken = default)
        {
            LastIntent = intent;
            return Task.FromResult(Result<bool>.Success(true));
        }
    }
}
