using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AccountChallengeDeliveryGateTests
{
    [TestMethod]
    [DataRow("unknown", false, false)]
    [DataRow("unknown", true, false)]
    [DataRow("rejected", true, false)]
    [DataRow("accepted", true, true)]
    [DataRow(null, false, true)]
    [DataRow(null, true, false)]
    public async Task Delivery_gate_requires_confirmation_and_preserves_legacy_expiry(
        string? state, bool completedOrLegacyExpired, bool expected)
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.CreateVersion7();
        var expired = state is null && completedOrLegacyExpired;
        var record = new AccountChallengeRecord(id, (byte)IdentityAccountChallengePurpose.RegistrationEmailVerification,
            "user@example.test", AccountChallengeCredentialHasher.Hash(id, "123456"),
            expired ? now.AddSeconds(-1) : now.AddMinutes(15), null, 0, 5, 1, now,
            state, state is not null && completedOrLegacyExpired ? now : null);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(record);
        var command = Substitute.For<ICommandExecutor>();
        var writes = new List<SqlStatement>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => { writes.Add(call.ArgAt<SqlStatement>(0)); return 1; });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var service = new AccountChallengeService(query, command, Substitute.For<ICommandTransaction>(),
            Substitute.For<IIdentityChallengeDeliveryPort>(), clock, Substitute.For<IIdGenerator>());
        var result = await service.ConsumeAsync(id, IdentityAccountChallengePurpose.RegistrationEmailVerification,
            "user@example.test", "123456");
        Assert.AreEqual(expected, result.IsSuccess);
        Assert.AreEqual(expected, writes.Contains(AccountChallengeSql.Consume));
        Assert.AreEqual(state is "unknown" or "rejected", writes.Contains(AccountChallengeSql.ReconcileDelivery));
        Assert.IsFalse(writes.Contains(AccountChallengeSql.CompleteDelivery), "消费与旧兼容不能补造受理事实。");
        Assert.AreEqual(expired ? now.AddSeconds(-1) : now.AddMinutes(15), record.ExpiresAtUtc);
    }
}
