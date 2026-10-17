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

/// <summary>真实双库验证未知状态消费防线、独立提交、补偿失败与按标识对账；网络协议由受控 SMTP 集负责。</summary>
[TestClass]
public sealed class AccountChallengeDeliveryJournalTests
{
    [TestMethod]
    public Task SqlServer_preserves_delivery_journal_and_reconciles_without_resending() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_preserves_delivery_journal_and_reconciles_without_resending() => VerifyAsync(DatabaseProvider.MySql);

    private async Task VerifyAsync(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.MySql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync(TestContext.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        await using var observerScope = factory.Services.CreateAsyncScope();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var query = observerScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var clock = new MutableClock();
        var recoveryUser = Guid.CreateVersion7();
        foreach (var purpose in new[] { IdentityAccountChallengePurpose.RegistrationEmailVerification,
            IdentityAccountChallengePurpose.InvitationEmailVerification, IdentityAccountChallengePurpose.PasswordRecovery })
        {
            foreach (var mode in new[] { "compensation-failed", "confirmation-failed", "confirmation-zero" })
            {
                clock.UtcNow = DateTimeOffset.UtcNow;
                var delivery = new DeliveryPort { Accepted = mode != "compensation-failed" };
                var broken = Service(new FaultingCommand(command, mode), delivery);
                var normal = Service(command, delivery);
                var email = $"journal-{Guid.NewGuid():N}@example.test";
                if (mode == "confirmation-zero")
                    Assert.IsFalse((await Create(broken, purpose, email)).IsSuccess);
                else
                    await Assert.ThrowsExactlyAsync<IOException>(() => Create(broken, purpose, email));
                var intent = delivery.Intent!;
                var record = await Read(intent.ChallengeId);
                Assert.AreEqual("unknown", record.DeliveryStateKey);
                Assert.AreEqual(mode == "compensation-failed", record.DeliveryCompletedAtUtc.HasValue);
                Assert.AreEqual(mode == "confirmation-zero", record.ConsumedAtUtc.HasValue);
                // 使用正确摘要和版本直接执行消费 SQL，证明防线不依赖服务层的先前读取。
                Assert.AreEqual(0, await command.ExecuteAsync(AccountChallengeSql.Consume,
                    IdentitySqlParameters.Create(("ChallengeId", record.ChallengeId), ("ConsumedAtUtc", clock.UtcNow),
                        ("CredentialHash", record.CredentialHash), ("Version", record.Version)), TestContext.CancellationToken));
                var consume = await normal.ConsumeAsync(intent.ChallengeId, purpose, email, intent.Credential,
                    TestContext.CancellationToken, recoveryUser, "trusted-stamp");
                Assert.IsFalse(consume.IsSuccess);
                Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, consume.Error!.Code);
                if (mode == "confirmation-failed")
                {
                    Assert.IsNull((await Read(record.ChallengeId)).ConsumedAtUtc,
                        "有效但尚无完成事实的未知记录不得被按需对账提前撤销。");
                    clock.UtcNow = record.ExpiresAtUtc.AddSeconds(1);
                    Assert.IsTrue(await normal.ReconcileUnconfirmedAsync(record.ChallengeId, TestContext.CancellationToken));
                }
                var reconciled = await Read(record.ChallengeId);
                Assert.IsNotNull(reconciled.ConsumedAtUtc);
                Assert.IsNotNull(reconciled.DeliveryReconciledAtUtc);
                Assert.AreEqual(2, reconciled.Version);
                Assert.IsFalse(await normal.ReconcileUnconfirmedAsync(record.ChallengeId, TestContext.CancellationToken));
                Assert.AreEqual(1, delivery.Sends, "对账只能修复本地状态，不能重发邮件。");
                delivery.Accepted = true;
                var replacement = await Create(normal, purpose, email);
                Assert.IsTrue(replacement.IsSuccess);
                Assert.IsFalse(await normal.ReconcileUnconfirmedAsync(record.ChallengeId, TestContext.CancellationToken));
                var current = await Read(replacement.Value!.ChallengeId);
                Assert.AreEqual("accepted", current.DeliveryStateKey);
                Assert.IsNull(current.ConsumedAtUtc, "旧标识迟到对账不能撤销同邮箱的新挑战。");
            }

            clock.UtcNow = DateTimeOffset.UtcNow;
            var paused = new DeliveryPort { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            var inFlightService = Service(command, paused);
            var pending = Create(inFlightService, purpose, $"inflight-{Guid.NewGuid():N}@example.test");
            await paused.Sent.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Result<AccountChallengeAcceptedResponse>? late = null;
            try
            {
                var record = await Read(paused.Intent!.ChallengeId);
                Assert.AreEqual("unknown", record.DeliveryStateKey, "独立读作用域必须在外发暂停时观察到已提交状态。");
                Assert.IsNull(record.DeliveryCompletedAtUtc);
                Assert.IsFalse(await inFlightService.ReconcileUnconfirmedAsync(record.ChallengeId, TestContext.CancellationToken));
                Assert.IsNull((await Read(record.ChallengeId)).ConsumedAtUtc);
                clock.UtcNow = record.ExpiresAtUtc.AddSeconds(1);
                Assert.IsTrue(await inFlightService.ReconcileUnconfirmedAsync(record.ChallengeId, TestContext.CancellationToken));
            }
            finally
            {
                paused.Gate!.TrySetResult(Result<bool>.Success(true));
                late = await pending;
            }
            Assert.IsFalse(late.IsSuccess, "到期对账后迟到受理不能重开凭据或报告真实受理成功。");
            var parked = await Read(paused.Intent!.ChallengeId);
            Assert.AreEqual("unknown", parked.DeliveryStateKey);
            Assert.IsNotNull(parked.DeliveryReconciledAtUtc);
            Assert.AreEqual(1, paused.Sends);

            clock.UtcNow = DateTimeOffset.UtcNow;
            var legacyPort = new DeliveryPort();
            var legacyService = Service(command, legacyPort);
            var legacyResult = await Create(legacyService, purpose, $"legacy-{Guid.NewGuid():N}@example.test");
            Assert.IsTrue(legacyResult.IsSuccess);
            var legacyId = legacyResult.Value!.ChallengeId;
            var old = await Read(legacyId);
            await command.ExecuteAsync(new SqlStatement("integration.legacy_challenge_delivery", """
                UPDATE fn_identity_account_challenge SET DeliveryStateKey = NULL,
                    DeliveryCompletedAtUtc = NULL WHERE ChallengeId = @Id
                """, SqlDataScope.Global), IdentitySqlParameters.Create(("Id", legacyId)), TestContext.CancellationToken);
            Assert.IsFalse(await legacyService.ReconcileUnconfirmedAsync(legacyId, TestContext.CancellationToken));
            Assert.IsTrue((await legacyService.ConsumeAsync(legacyId, purpose, legacyPort.Intent!.NormalizedEmail,
                legacyPort.Intent.Credential, TestContext.CancellationToken, recoveryUser, "trusted-stamp")).IsSuccess);
            var consumedLegacy = await Read(legacyId);
            Assert.IsNull(consumedLegacy.DeliveryStateKey);
            Assert.AreEqual(old.ExpiresAtUtc, consumedLegacy.ExpiresAtUtc);
        }

        AccountChallengeService Service(ICommandExecutor writer, DeliveryPort delivery) => new(query, writer, transaction,
            delivery, clock, new TestIds());
        Task<Result<AccountChallengeAcceptedResponse>> Create(AccountChallengeService service,
            IdentityAccountChallengePurpose purpose, string email) => service.CreateAndDeliverAsync(purpose, email,
                TestContext.CancellationToken, recoveryUser, "trusted-stamp");
        async Task<AccountChallengeRecord> Read(Guid id) => await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
            AccountChallengeSql.FindById, IdentitySqlParameters.Create(("ChallengeId", id)), TestContext.CancellationToken)
            ?? throw new AssertFailedException("挑战记录必须真实存在。");
    }

    private sealed class FaultingCommand(ICommandExecutor inner, string mode) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement.Equals(AccountChallengeSql.CompleteDelivery) && mode == "confirmation-zero") return Task.FromResult(0);
            if ((statement.Equals(AccountChallengeSql.CompleteDelivery) && mode == "confirmation-failed")
                || (statement.Equals(AccountChallengeSql.InvalidateById) && mode != "confirmation-zero"))
                throw new IOException("controlled-journal-write-failure");
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
    private sealed class DeliveryPort : IIdentityChallengeDeliveryPort
    {
        public bool Accepted { get; set; } = true;
        public TaskCompletionSource<Result<bool>>? Gate { get; init; }
        public TaskCompletionSource<bool> Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IdentityChallengeDeliveryIntent? Intent { get; private set; }
        public int Sends { get; private set; }
        public Task<Result<bool>> SendAsync(IdentityChallengeDeliveryIntent intent, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sends++;
            Intent = intent;
            Sent.TrySetResult(true);
            return Gate?.Task ?? Task.FromResult(Result<bool>.Success(Accepted));
        }
    }
    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow; }
    private sealed class TestIds : IIdGenerator { public Guid NewId() => Guid.CreateVersion7(); }
    public TestContext TestContext { get; set; } = null!;
}
