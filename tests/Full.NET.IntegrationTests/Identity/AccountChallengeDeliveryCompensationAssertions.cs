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
    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var delivery = new DelayedFailureDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services => services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery));
        await scopedFactory.InitializeAsync();
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
            // 旧请求提交后暂停外部投递，新请求在独立作用域提交并成功投递，再释放旧失败。
            var pending = firstService.CreateAndDeliverAsync(purpose, email.ToUpperInvariant());
            Result<AccountChallengeAcceptedResponse>? failed = null;
            IdentityChallengeDeliveryIntent? oldIntent = null;
            IdentityChallengeDeliveryIntent? newIntent = null;
            try
            {
                oldIntent = await gate.Sent.Task.WaitAsync(TimeSpan.FromSeconds(30));
                var replacement = await secondService.CreateAndDeliverAsync(purpose, email);
                Assert.IsTrue(replacement.IsSuccess);
                newIntent = delivery.LastIntent;
                Assert.IsNotNull(newIntent);
                Assert.AreEqual(replacement.Value!.ChallengeId, newIntent.ChallengeId);
                Assert.AreNotEqual(oldIntent.ChallengeId, newIntent.ChallengeId);
            }
            finally
            {
                gate.Complete.TrySetResult(Result<bool>.Failure(new Error(
                    IdentityErrorCodes.AccountChallengeDeliveryFailed, "Test delivery rejected.", ErrorType.BusinessRule)));
                failed = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            }

            Assert.IsFalse(failed.IsSuccess);
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, failed.Error!.Code);
            var current = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                IdentitySqlParameters.Create(("ChallengeId", newIntent!.ChallengeId)));
            Assert.IsNotNull(current);
            Assert.IsNull(current.ConsumedAtUtc, "旧请求的迟到失败不得撤销成功重发的新挑战。");
            Assert.AreEqual(1, current.Version);
            Assert.AreNotEqual(newIntent.Credential, current.CredentialHash);

            var old = await firstService.ConsumeAsync(oldIntent!.ChallengeId, purpose, email, oldIntent.Credential);
            Assert.IsFalse(old.IsSuccess);
            var wrongPurpose = purpose == IdentityAccountChallengePurpose.PasswordRecovery
                ? IdentityAccountChallengePurpose.RegistrationEmailVerification
                : IdentityAccountChallengePurpose.PasswordRecovery;
            var wrong = await secondService.ConsumeAsync(newIntent.ChallengeId, wrongPurpose, email, newIntent.Credential);
            Assert.IsFalse(wrong.IsSuccess);
            var accepted = await secondService.ConsumeAsync(newIntent.ChallengeId, purpose, email, newIntent.Credential);
            Assert.IsTrue(accepted.IsSuccess, "成功重发的新挑战应可消费一次。");
            var replay = await secondService.ConsumeAsync(newIntent.ChallengeId, purpose, email, newIntent.Credential);
            Assert.IsFalse(replay.IsSuccess);

            // 无重发时失败挑战仍活跃，验证补偿实际更新一行，而非仅验证迟到失败的零行路径。
            var standaloneGate = delivery.PauseNext();
            var standaloneEmail = $"failed-{Guid.NewGuid():N}@example.test";
            var standalone = firstService.CreateAndDeliverAsync(purpose, standaloneEmail);
            IdentityChallengeDeliveryIntent? failedIntent = null;
            try
            {
                failedIntent = await standaloneGate.Sent.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                standaloneGate.Complete.TrySetResult(Result<bool>.Failure(new Error(
                    IdentityErrorCodes.AccountChallengeDeliveryFailed, "Test delivery rejected.", ErrorType.BusinessRule)));
                failed = await standalone.WaitAsync(TimeSpan.FromSeconds(30));
            }

            Assert.IsFalse(failed.IsSuccess);
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, failed.Error!.Code);
            var compensated = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                IdentitySqlParameters.Create(("ChallengeId", failedIntent!.ChallengeId)));
            Assert.IsNotNull(compensated);
            Assert.IsNotNull(compensated.ConsumedAtUtc);
            Assert.AreEqual(2, compensated.Version);
            var rejected = await firstService.ConsumeAsync(failedIntent.ChallengeId, purpose, standaloneEmail, failedIntent.Credential);
            Assert.IsFalse(rejected.IsSuccess);
        }
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
            return gate.Complete.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DeliveryGate
    {
        public TaskCompletionSource<IdentityChallengeDeliveryIntent> Sent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Result<bool>> Complete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
