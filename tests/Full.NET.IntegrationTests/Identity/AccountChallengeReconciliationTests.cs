using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实双库验证主键分页、未知投递巡检、并发受理保护及后续重扫；纯配置与取消在单元层验证。</summary>
[TestClass]
public sealed class AccountChallengeReconciliationTests
{
    [TestMethod]
    public Task SqlServer_scans_bounded_pages_and_revisits_changes_without_resending() => Verify(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_scans_bounded_pages_and_revisits_changes_without_resending() => Verify(DatabaseProvider.MySql);

    private async Task Verify(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.MySql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync(TestContext.CancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var now = DateTimeOffset.UtcNow;
        var clock = new FrozenClock(now);
        var ids = new Dictionary<string, Guid>();
        foreach (var scenario in new[] { "completed", "expired", "rejected", "consumed", "inflight", "accepted", "legacy", "reconciled", "racing-accept" })
        {
            var id = Guid.CreateVersion7(); ids.Add(scenario, id);
            await command.ExecuteAsync(AccountChallengeSql.Insert, IdentitySqlParameters.Create(
                ("ChallengeId", id), ("Purpose", (byte)1), ("NormalizedEmail", $"{id:N}@example.test"), ("CredentialHash", AccountChallengeCredentialHasher.Hash(id, "123456")),
                ("ExpiresAtUtc", now.AddMinutes(scenario is "expired" or "legacy" or "racing-accept" ? -1 : 15)),
                ("MaxAttempts", 5), ("DeliveryStateKey", scenario == "legacy" ? null : "unknown"), ("CreatedAtUtc", now)), TestContext.CancellationToken);
            if (scenario is "completed" or "rejected" or "consumed" or "accepted" or "reconciled")
                await command.ExecuteAsync(AccountChallengeSql.CompleteDelivery, IdentitySqlParameters.Create(("ChallengeId", id),
                    ("CompletedAtUtc", now), ("DeliveryStateKey", scenario is "rejected" or "accepted" ? scenario : "unknown")), TestContext.CancellationToken);
            if (scenario is "consumed" or "reconciled")
                await command.ExecuteAsync(new SqlStatement("test.challenge_history", """
                    UPDATE fn_identity_account_challenge SET ConsumedAtUtc = @Consumed,
                        DeliveryReconciledAtUtc = @Reconciled, Version = 7 WHERE ChallengeId = @ChallengeId
                    """, SqlDataScope.Global), IdentitySqlParameters.Create(("ChallengeId", id), ("Consumed", now.AddSeconds(-10)),
                        ("Reconciled", scenario == "reconciled" ? now.AddSeconds(-10) : null)), TestContext.CancellationToken);
        }
        var racing = new AcceptBeforeReconciliation(command, ids["racing-accept"], now);
        var runner = new AccountChallengeReconciliationRunner(query, racing, clock, Options.Create(new DatabaseOptions { Provider = provider }));
        Assert.AreEqual(4, await ScanAll());
        foreach (var scenario in new[] { "completed", "expired", "rejected", "consumed" })
        {
            var row = await Read(ids[scenario]);
            Assert.IsNotNull(row.DeliveryReconciledAtUtc); Assert.IsNotNull(row.ConsumedAtUtc);
            Assert.AreEqual(scenario == "consumed" ? 7 : 2, row.Version);
            if (scenario == "consumed") Assert.AreEqual(now.AddSeconds(-10).ToUnixTimeMilliseconds(), row.ConsumedAtUtc!.Value.ToUnixTimeMilliseconds());
            Assert.AreEqual(0, await command.ExecuteAsync(AccountChallengeSql.CompleteDelivery,
                IdentitySqlParameters.Create(("ChallengeId", row.ChallengeId), ("CompletedAtUtc", now), ("DeliveryStateKey", "accepted")), TestContext.CancellationToken));
        }
        foreach (var scenario in new[] { "inflight", "accepted", "legacy", "racing-accept" })
        {
            var row = await Read(ids[scenario]);
            Assert.IsNull(row.DeliveryReconciledAtUtc); Assert.IsNull(row.ConsumedAtUtc); Assert.AreEqual(1, row.Version);
        }
        Assert.AreEqual("accepted", (await Read(ids["racing-accept"])).DeliveryStateKey);
        Assert.AreEqual(0, await ScanAll(), "重复巡检不能重复修改状态或版本。");
        await command.ExecuteAsync(AccountChallengeSql.CompleteDelivery, IdentitySqlParameters.Create(("ChallengeId", ids["inflight"]),
            ("CompletedAtUtc", now), ("DeliveryStateKey", "unknown")), TestContext.CancellationToken);
        Assert.AreEqual(1, await ScanAll(), "已越过游标后才完成的未知投递必须在下一次遍历收敛。");
        Assert.AreEqual(0, await ScanAll());

        async Task<int> ScanAll()
        {
            Guid? afterId = null;
            var total = 0; var cursors = new HashSet<Guid>();
            for (var pageNumber = 0; pageNumber < 100; pageNumber++)
            {
                var page = await runner.RunPageAsync(afterId, 3, TestContext.CancellationToken);
                Assert.IsTrue(page.Scanned <= 3); total += page.Reconciled;
                if (!page.NextAfterId.HasValue) return total;
                Assert.IsTrue(cursors.Add(page.NextAfterId.Value), "主键分页不能重复游标或停滞。");
                afterId = page.NextAfterId;
            }
            Assert.Fail("巡检未在受控样本范围内完成。"); return -1;
        }
        async Task<AccountChallengeRecord> Read(Guid id) => await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
            AccountChallengeSql.FindById, IdentitySqlParameters.Create(("ChallengeId", id)), TestContext.CancellationToken)
            ?? throw new InvalidOperationException("Challenge sample missing.");
    }

    private sealed class FrozenClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private sealed class AcceptBeforeReconciliation(ICommandExecutor inner, Guid id, DateTimeOffset now) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement == AccountChallengeSql.ReconcileDelivery && parameters is IReadOnlyDictionary<string, object?> values && Equals(values["ChallengeId"], id))
                await inner.ExecuteAsync(AccountChallengeSql.CompleteDelivery,
                    IdentitySqlParameters.Create(("ChallengeId", id), ("CompletedAtUtc", now), ("DeliveryStateKey", "accepted")), cancellationToken);
            return await inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
    public TestContext TestContext { get; set; } = null!;
}
