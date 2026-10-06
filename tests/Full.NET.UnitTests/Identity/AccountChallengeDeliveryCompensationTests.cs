using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Notifications.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AccountChallengeDeliveryCompensationTests
{
    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification)]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery)]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification)]
    public async Task Late_delivery_failure_compensates_only_the_failed_request(
        IdentityAccountChallengePurpose purpose)
    {
        var fixture = new Fixture();
        var first = fixture.Service.CreateAndDeliverAsync(purpose, " User@Example.test ");
        await fixture.FirstSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var second = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test");
            Assert.IsTrue(second.IsSuccess);
            Assert.AreNotEqual(fixture.Intents[0].ChallengeId, second.Value!.ChallengeId);
        }
        finally
        {
            fixture.FirstDelivery.TrySetResult(Failure());
        }

        var result = await first;
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, result.Error!.Code);
        AssertFailedRequestScope(fixture);
        Assert.AreEqual(2, fixture.TransactionCount);
    }

    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification, 0)]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification, 1)]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery, 0)]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery, 1)]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification, 0)]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification, 1)]
    public async Task Failed_delivery_keeps_failure_when_compensation_finds_no_active_row(
        IdentityAccountChallengePurpose purpose, int affectedRows)
    {
        var fixture = new Fixture { CompensationAffectedRows = affectedRows };
        fixture.FirstDelivery.SetResult(Failure());
        var result = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, result.Error!.Code);
        AssertFailedRequestScope(fixture);
        Assert.AreEqual(1, fixture.TransactionCount);
    }

    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification)]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery)]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification)]
    public async Task Successful_delivery_does_not_run_failure_compensation(
        IdentityAccountChallengePurpose purpose)
    {
        var fixture = new Fixture();
        fixture.FirstDelivery.SetResult(Result<bool>.Success(true));
        var result = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(fixture.Intents[0].ChallengeId, result.Value!.ChallengeId);
        Assert.AreEqual(2, fixture.Writes.Count);
        Assert.AreEqual(1, fixture.TransactionCount);
    }

    private static void AssertFailedRequestScope(Fixture fixture)
    {
        // 数据库语义由双库回归验证；此处锁定补偿拥有的请求标识及外部投递的事务边界。
        var parameters = fixture.Writes[^1];
        Assert.IsTrue(parameters.TryGetValue("ChallengeId", out var id),
            "投递失败补偿必须限定到当前请求的 ChallengeId。");
        Assert.AreEqual(fixture.Intents[0].ChallengeId, id);
        Assert.IsFalse(fixture.DeliveryInsideTransaction);
    }

    private static Result<bool> Failure() => Result<bool>.Failure(new Error(
        IdentityErrorCodes.AccountChallengeDeliveryFailed, "Test delivery rejected.", ErrorType.BusinessRule));

    private sealed class Fixture
    {
        public AccountChallengeService Service { get; }
        public TaskCompletionSource<bool> FirstSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Result<bool>> FirstDelivery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IdentityChallengeDeliveryIntent> Intents { get; } = [];
        public List<IReadOnlyDictionary<string, object?>> Writes { get; } = [];
        public int CompensationAffectedRows { get; init; } = 1;
        public int TransactionCount { get; private set; }
        public bool DeliveryInsideTransaction { get; private set; }
        private bool insideTransaction;

        public Fixture()
        {
            var query = Substitute.For<IQueryExecutor>();
            var command = Substitute.For<ICommandExecutor>();
            var transaction = Substitute.For<ICommandTransaction>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTimeOffset(2026, 10, 6, 0, 0, 0, TimeSpan.Zero));
            var ids = Substitute.For<IIdGenerator>();
            ids.NewId().Returns(_ => Guid.CreateVersion7());
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var values = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
                    Writes.Add(new Dictionary<string, object?>(values));
                    return Task.FromResult(insideTransaction ? 1 : CompensationAffectedRows);
                });
            // 仅替换数据库事务边界；实际服务负责创建、外部等待及失败补偿的顺序。
            transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    TransactionCount++;
                    insideTransaction = true;
                    try { return await call.ArgAt<Func<CancellationToken, Task<bool>>>(0)(call.ArgAt<CancellationToken>(1)); }
                    finally { insideTransaction = false; }
                });
            var delivery = Substitute.For<IIdentityChallengeDeliveryPort>();
            delivery.SendAsync(Arg.Any<IdentityChallengeDeliveryIntent>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    DeliveryInsideTransaction |= insideTransaction;
                    Intents.Add(call.ArgAt<IdentityChallengeDeliveryIntent>(0));
                    if (Intents.Count != 1) return Task.FromResult(Result<bool>.Success(true));
                    FirstSent.TrySetResult(true);
                    return FirstDelivery.Task;
                });
            Service = new AccountChallengeService(query, command, transaction, delivery, clock, ids);
        }
    }
}
