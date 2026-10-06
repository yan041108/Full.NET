using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.RecoverAccount;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class PasswordRecoveryResponseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("inactive")]
    [DataRow("invalid-email")]
    [DataRow("delivery-failure")]
    [DataRow("delivery-exception")]
    [DataRow("delivery-timeout")]
    [DataRow("delivery-not-accepted")]
    public async Task Non_delivered_recovery_keeps_the_accepted_response_shape(string scenario)
    {
        var fixture = new Fixture(scenario);
        var email = scenario == "invalid-email" ? "invalid" : "user@example.test";
        var result = await fixture.Handler.HandleAsync(new RequestCommand(new(email)), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreNotEqual(Guid.Empty, result.Value!.ChallengeId,
            "匿名恢复响应不能用空标识暴露账号或投递状态。");
        Assert.AreEqual(7, result.Value.ChallengeId.Version);
        Assert.AreEqual(Now.AddMinutes(15), result.Value.ExpiresAtUtc);
        if (scenario.StartsWith("delivery-", StringComparison.Ordinal))
        {
            Assert.AreEqual(1, fixture.Intents.Count);
            Assert.AreNotEqual(fixture.Intents[0].ChallengeId, result.Value.ChallengeId);
            Assert.AreEqual(3, fixture.Writes.Count);
            Assert.AreEqual(fixture.Intents[0].ChallengeId, fixture.Writes[^1]["ChallengeId"]);
        }
        else
        {
            Assert.AreEqual(0, fixture.Intents.Count);
            Assert.AreEqual(0, fixture.Writes.Count, "占位响应不得创建任何真实挑战。");
        }
    }

    [TestMethod]
    public async Task Delivered_recovery_preserves_the_usable_challenge_and_expiry()
    {
        var fixture = new Fixture("active");
        var result = await fixture.Handler.HandleAsync(new RequestCommand(new("user@example.test")), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, fixture.Intents.Count);
        Assert.AreEqual(fixture.Intents[0].ChallengeId, result.Value!.ChallengeId);
        Assert.AreEqual(fixture.Intents[0].ExpiresAtUtc, result.Value.ExpiresAtUtc);
        Assert.AreEqual(Now.AddMinutes(15), result.Value.ExpiresAtUtc);
        Assert.AreEqual(2, fixture.Writes.Count);
    }

    private sealed class Fixture
    {
        public RequestHandler Handler { get; }
        public List<IdentityChallengeDeliveryIntent> Intents { get; } = [];
        public List<IReadOnlyDictionary<string, object?>> Writes { get; } = [];

        public Fixture(string scenario)
        {
            var query = Substitute.For<IQueryExecutor>();
            var user = scenario == "unknown" ? null : new IdentityUserRecord
            {
                Id = Guid.CreateVersion7(),
                IsActive = scenario != "inactive",
                SecurityStamp = "trusted-test-stamp",
            };
            query.QuerySingleOrDefaultAsync<IdentityUserRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(user));
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Writes.Add(new Dictionary<string, object?>((IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1)));
                return Task.FromResult(1);
            });
            var transaction = Substitute.For<ICommandTransaction>();
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Func<CancellationToken, Task<bool>>>(0)(call.ArgAt<CancellationToken>(1)));
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            var delivery = Substitute.For<IIdentityChallengeDeliveryPort>();
            delivery.SendAsync(Arg.Any<IdentityChallengeDeliveryIntent>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                Intents.Add(call.ArgAt<IdentityChallengeDeliveryIntent>(0));
                if (scenario == "delivery-exception") throw new IOException("sensitive-delivery-detail");
                if (scenario == "delivery-timeout") throw new OperationCanceledException("sensitive-delivery-detail");
                if (scenario == "delivery-not-accepted") return Task.FromResult(Result<bool>.Success(false));
                return Task.FromResult(scenario == "delivery-failure"
                    ? Result<bool>.Failure(new Error(IdentityErrorCodes.AccountChallengeDeliveryFailed, "Test delivery rejected.", ErrorType.BusinessRule))
                    : Result<bool>.Success(true));
            });
            Handler = new RequestHandler(new AccountChallengeService(query, command, transaction, delivery, clock, ids), query);
        }
    }
}
