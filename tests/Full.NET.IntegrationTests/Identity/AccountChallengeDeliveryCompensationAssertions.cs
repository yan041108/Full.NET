using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class AccountChallengeDeliveryCompensationAssertions
{
    // 纯挑战夹具提供稳定恢复账号；注册和邀请用途仍使用原摘要。
    private static readonly Guid RecoveryUserId = Guid.Parse("018f5f40-0000-7000-8000-000000000123");

    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var delivery = new DelayedFailureDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services => services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery));
        await scopedFactory.InitializeAsync();
        foreach (var outcome in new[] { "failure", "exception", "timeout", "not-accepted",
            "caller-cancellation", "canceled-failure", "canceled-not-accepted" })
        {
            foreach (var purpose in new[]
            {
                IdentityAccountChallengePurpose.RegistrationEmailVerification,
                IdentityAccountChallengePurpose.PasswordRecovery,
                IdentityAccountChallengePurpose.InvitationEmailVerification,
            })
            {
                var email = $"challenge-{Guid.NewGuid():N}@example.test";
                await using var firstScope = scopedFactory.Services.CreateAsyncScope();
                await using var secondScope = scopedFactory.Services.CreateAsyncScope();
                var firstService = firstScope.ServiceProvider.GetRequiredService<AccountChallengeService>();
                var secondService = secondScope.ServiceProvider.GetRequiredService<AccountChallengeService>();
                var query = secondScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
                var gate = delivery.PauseNext();
                gate.ObserveCallerCancellation = !outcome.StartsWith("canceled-", StringComparison.Ordinal);
                using var cancellation = new CancellationTokenSource();
                // 旧请求提交后暂停外部投递，新请求在独立作用域提交并成功投递，再释放旧失败。
                var pending = firstService.CreateAndDeliverAsync(purpose, email.ToUpperInvariant(), cancellation.Token, RecoveryUserId);
                Result<AccountChallengeAcceptedResponse>? failed = null;
                var cancellationExpected = outcome.Contains("cancel", StringComparison.Ordinal);
                Exception? cancellationError = null;
                IdentityChallengeDeliveryIntent? oldIntent = null;
                IdentityChallengeDeliveryIntent? newIntent = null;
                try
                {
                    oldIntent = await gate.Sent.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    var replacement = await secondService.CreateAndDeliverAsync(purpose, email, recoveryUserId: RecoveryUserId);
                    Assert.IsTrue(replacement.IsSuccess);
                    newIntent = delivery.LastIntent;
                    Assert.IsNotNull(newIntent);
                    Assert.AreEqual(replacement.Value!.ChallengeId, newIntent.ChallengeId);
                    Assert.AreNotEqual(oldIntent.ChallengeId, newIntent.ChallengeId);
                }
                finally
                {
                    CompleteDelivery(gate, outcome, cancellation);
                    if (cancellationExpected)
                    {
                        // finally 只释放和等待，验收断言放在清理后，避免覆盖重发阶段的原始失败。
                        try { await pending.WaitAsync(TimeSpan.FromSeconds(30)); }
                        catch (Exception error) { cancellationError = error; }
                    }
                    else failed = await pending.WaitAsync(TimeSpan.FromSeconds(30));
                }

                if (cancellationExpected)
                {
                    Assert.IsInstanceOfType<OperationCanceledException>(cancellationError);
                    Assert.AreEqual(cancellation.Token, ((OperationCanceledException)cancellationError).CancellationToken);
                }
                else if (failed is not null)
                {
                    Assert.IsFalse(failed.IsSuccess);
                    Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, failed.Error!.Code);
                }
                var current = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                    IdentitySqlParameters.Create(("ChallengeId", newIntent!.ChallengeId)));
                Assert.IsNotNull(current);
                Assert.IsNull(current.ConsumedAtUtc, "旧请求的迟到失败不得撤销成功重发的新挑战。");
                Assert.AreEqual(1, current.Version);
                Assert.AreNotEqual(newIntent.Credential, current.CredentialHash);

                var old = await firstService.ConsumeAsync(oldIntent!.ChallengeId, purpose, email, oldIntent.Credential, recoveryUserId: RecoveryUserId);
                Assert.IsFalse(old.IsSuccess);
                var wrongPurpose = purpose == IdentityAccountChallengePurpose.PasswordRecovery
                    ? IdentityAccountChallengePurpose.RegistrationEmailVerification
                    : IdentityAccountChallengePurpose.PasswordRecovery;
                var wrong = await secondService.ConsumeAsync(newIntent.ChallengeId, wrongPurpose, email, newIntent.Credential, recoveryUserId: RecoveryUserId);
                Assert.IsFalse(wrong.IsSuccess);
                var accepted = await secondService.ConsumeAsync(newIntent.ChallengeId, purpose, email, newIntent.Credential, recoveryUserId: RecoveryUserId);
                Assert.IsTrue(accepted.IsSuccess, "成功重发的新挑战应可消费一次。");
                var replay = await secondService.ConsumeAsync(newIntent.ChallengeId, purpose, email, newIntent.Credential, recoveryUserId: RecoveryUserId);
                Assert.IsFalse(replay.IsSuccess);

                // 无重发时失败挑战仍活跃，验证补偿实际更新一行，而非仅验证迟到失败的零行路径。
                var standaloneGate = delivery.PauseNext();
                standaloneGate.ObserveCallerCancellation = gate.ObserveCallerCancellation;
                using var standaloneCancellation = new CancellationTokenSource();
                var standaloneEmail = $"failed-{Guid.NewGuid():N}@example.test";
                var standalone = firstService.CreateAndDeliverAsync(purpose, standaloneEmail, standaloneCancellation.Token, RecoveryUserId);
                IdentityChallengeDeliveryIntent? failedIntent = null;
                Exception? standaloneCancellationError = null;
                try
                {
                    failedIntent = await standaloneGate.Sent.Task.WaitAsync(TimeSpan.FromSeconds(30));
                }
                finally
                {
                    CompleteDelivery(standaloneGate, outcome, standaloneCancellation);
                    if (cancellationExpected)
                    {
                        try { await standalone.WaitAsync(TimeSpan.FromSeconds(30)); }
                        catch (Exception error) { standaloneCancellationError = error; }
                    }
                    else failed = await standalone.WaitAsync(TimeSpan.FromSeconds(30));
                }

                if (cancellationExpected)
                {
                    Assert.IsInstanceOfType<OperationCanceledException>(standaloneCancellationError);
                    Assert.AreEqual(standaloneCancellation.Token, ((OperationCanceledException)standaloneCancellationError).CancellationToken);
                }
                else if (failed is not null)
                {
                    Assert.IsFalse(failed.IsSuccess);
                    Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, failed.Error!.Code);
                }
                var compensated = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                    IdentitySqlParameters.Create(("ChallengeId", failedIntent!.ChallengeId)));
                Assert.IsNotNull(compensated);
                Assert.IsNotNull(compensated.ConsumedAtUtc);
                Assert.AreEqual(2, compensated.Version);
                var rejected = await firstService.ConsumeAsync(failedIntent.ChallengeId, purpose, standaloneEmail, failedIntent.Credential, recoveryUserId: RecoveryUserId);
                Assert.IsFalse(rejected.IsSuccess);
            }
        }
    }

    // 每种用途同时验证明确失败、异常、内部超时及未受理，补偿必须只影响当前请求。
    private static void CompleteDelivery(DeliveryGate gate, string outcome, CancellationTokenSource cancellation)
    {
        // 请求取消后，分别模拟遵守取消的传输和仍返回明确失败的传输，覆盖两条补偿入口。
        if (outcome.Contains("cancel", StringComparison.Ordinal)) cancellation.Cancel();
        if (outcome == "caller-cancellation") { gate.Complete.TrySetCanceled(cancellation.Token); return; }
        if (outcome == "exception") gate.Complete.TrySetException(new IOException("sensitive-delivery-detail"));
        else if (outcome == "timeout") gate.Complete.TrySetException(new OperationCanceledException("sensitive-delivery-detail"));
        else gate.Complete.TrySetResult(outcome is "not-accepted" or "canceled-not-accepted"
            ? Result<bool>.Success(false)
            : Result<bool>.Failure(new Error(IdentityErrorCodes.AccountChallengeDeliveryFailed,
                "Test delivery rejected.", ErrorType.BusinessRule)));
    }

    private sealed class DelayedFailureDeliveryPort : IIdentityChallengeDeliveryPort
    {
        private DeliveryGate? next;
        public IdentityChallengeDeliveryIntent? LastIntent { get; private set; }

        public DeliveryGate PauseNext()
        {
            var gate = new DeliveryGate();
            Assert.IsNull(Interlocked.CompareExchange(ref next, gate, null));
            return gate;
        }

        public Task<Result<bool>> SendAsync(IdentityChallengeDeliveryIntent intent,
            CancellationToken cancellationToken = default)
        {
            LastIntent = intent;
            var gate = Interlocked.Exchange(ref next, null);
            if (gate is null) return Task.FromResult(Result<bool>.Success(true));
            gate.Sent.TrySetResult(intent);
            return gate.ObserveCallerCancellation ? gate.Complete.Task.WaitAsync(cancellationToken) : gate.Complete.Task;
        }
    }

    private sealed class DeliveryGate
    {
        public bool ObserveCallerCancellation { get; set; } = true;
        public TaskCompletionSource<IdentityChallengeDeliveryIntent> Sent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Result<bool>> Complete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
