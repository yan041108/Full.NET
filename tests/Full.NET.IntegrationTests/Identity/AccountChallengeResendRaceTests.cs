using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实双库验证重发与延迟投递失败交错时，新验证码仍可且只能消费一次。</summary>
[TestClass]
public sealed class AccountChallengeResendRaceTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Late_delivery_failure_does_not_revoke_a_successful_resend(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var delivery = new GatedDelivery();
        using var factory = new FullNetApiFactory(provider, connection,
            configureTestServices: services => services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery));
        await factory.InitializeAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = timeout.Token;
        foreach (var purpose in Enum.GetValues<IdentityAccountChallengePurpose>())
        {
            var email = $"resend-{Guid.NewGuid():N}@example.test";
            delivery.Reset();
            await using var first = factory.Services.CreateAsyncScope();
            await using var second = factory.Services.CreateAsyncScope();
            first.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            second.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var oldService = first.ServiceProvider.GetRequiredService<AccountChallengeService>();
            var service = second.ServiceProvider.GetRequiredService<AccountChallengeService>();
            var oldRequest = oldService.CreateAndDeliverAsync(purpose, email, token);
            IdentityChallengeDeliveryIntent oldIntent;
            IdentityChallengeDeliveryIntent currentIntent;
            AccountChallengeRecord oldBefore;
            try
            {
                oldIntent = await delivery.FirstSent.Task.WaitAsync(token);
                var resent = await service.CreateAndDeliverAsync(purpose, email, token);
                Assert.IsTrue(resent.IsSuccess, resent.Error?.Code);
                currentIntent = delivery.Latest!;
                Assert.AreNotEqual(oldIntent.ChallengeId, currentIntent.ChallengeId);
                oldBefore = await ReadAsync(second.ServiceProvider, oldIntent.ChallengeId, token);
                Assert.IsNotNull(oldBefore.ConsumedAtUtc, "重发应先作废旧验证码。");
            }
            finally
            {
                delivery.ReleaseFailure();
            }
            var failed = await oldRequest;
            Assert.IsFalse(failed.IsSuccess);
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, failed.Error!.Code);
            var current = await ReadAsync(second.ServiceProvider, currentIntent.ChallengeId, token);
            Assert.IsNull(current.ConsumedAtUtc, "旧投递失败不得撤销新挑战。");
            Assert.AreEqual(1, current.Version);
            Assert.AreEqual(oldBefore.Version, (await ReadAsync(second.ServiceProvider, oldIntent.ChallengeId, token)).Version,
                "已经作废的旧挑战不能再次递增版本。");
            Assert.IsFalse((await service.ConsumeAsync(oldIntent.ChallengeId, purpose, email, oldIntent.Credential, token)).IsSuccess);
            Assert.IsTrue((await service.ConsumeAsync(currentIntent.ChallengeId, purpose, email, currentIntent.Credential, token)).IsSuccess);
            Assert.IsFalse((await service.ConsumeAsync(currentIntent.ChallengeId, purpose, email, currentIntent.Credential, token)).IsSuccess);

            // 没有后续重发时，失败投递仍必须撤销本次挑战。
            delivery.Reset();
            var standalone = service.CreateAndDeliverAsync(purpose, $"failed-{Guid.NewGuid():N}@example.test", token);
            var standaloneIntent = await delivery.FirstSent.Task.WaitAsync(token);
            delivery.ReleaseFailure();
            Assert.IsFalse((await standalone).IsSuccess);
            Assert.IsNotNull((await ReadAsync(second.ServiceProvider, standaloneIntent.ChallengeId, token)).ConsumedAtUtc);
        }
    }

    private static async Task<AccountChallengeRecord> ReadAsync(IServiceProvider services, Guid id, CancellationToken token)
    {
        var record = await services.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AccountChallengeRecord>(
            AccountChallengeSql.FindById, IdentitySqlParameters.Create(("ChallengeId", id)), token);
        Assert.IsNotNull(record);
        return record;
    }

    private sealed class GatedDelivery : IIdentityChallengeDeliveryPort
    {
        private int calls;
        private TaskCompletionSource<Result<bool>> completion = NewCompletion();
        public TaskCompletionSource<IdentityChallengeDeliveryIntent> FirstSent { get; private set; } = NewSent();
        public IdentityChallengeDeliveryIntent? Latest { get; private set; }

        public void Reset()
        {
            calls = 0;
            completion = NewCompletion();
            FirstSent = NewSent();
            Latest = null;
        }

        public void ReleaseFailure() => completion.TrySetResult(Result<bool>.Failure(new Error(
            IdentityErrorCodes.AccountChallengeDeliveryFailed, "Controlled delivery failure.", ErrorType.BusinessRule)));

        public Task<Result<bool>> SendAsync(IdentityChallengeDeliveryIntent intent, CancellationToken cancellationToken = default)
        {
            Latest = intent;
            if (Interlocked.Increment(ref calls) != 1) return Task.FromResult(Result<bool>.Success(true));
            FirstSent.TrySetResult(intent);
            return completion.Task.WaitAsync(cancellationToken);
        }

        private static TaskCompletionSource<Result<bool>> NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static TaskCompletionSource<IdentityChallengeDeliveryIntent> NewSent() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
