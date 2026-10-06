using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AccountChallengeDeliveryCompensationTests
{
    // 纯挑战夹具提供稳定恢复账号；注册和邀请用途仍使用原摘要。
    private static readonly Guid RecoveryUserId = Guid.Parse("018f5f40-0000-7000-8000-000000000123");

    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification)]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery)]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification)]
    public async Task Late_delivery_failure_compensates_only_the_failed_request(
        IdentityAccountChallengePurpose purpose)
    {
        var fixture = new Fixture();
        var first = fixture.Service.CreateAndDeliverAsync(purpose, " User@Example.test ", recoveryUserId: RecoveryUserId);
        await fixture.FirstSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var second = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test", recoveryUserId: RecoveryUserId);
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
        var result = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test", recoveryUserId: RecoveryUserId);
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
        var result = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test", recoveryUserId: RecoveryUserId);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(fixture.Intents[0].ChallengeId, result.Value!.ChallengeId);
        Assert.AreEqual(2, fixture.Writes.Count);
        Assert.AreEqual(1, fixture.TransactionCount);
    }

    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification, "exception")]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery, "exception")]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification, "exception")]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification, "timeout")]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery, "timeout")]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification, "timeout")]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification, "not-accepted")]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery, "not-accepted")]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification, "not-accepted")]
    public async Task Unaccepted_delivery_compensates_current_challenge_without_exposing_exception(
        IdentityAccountChallengePurpose purpose, string outcome)
    {
        var fixture = new Fixture();
        if (outcome == "not-accepted") fixture.FirstDelivery.SetResult(Result<bool>.Success(false));
        else fixture.FirstDelivery.SetException(outcome == "timeout"
            ? new OperationCanceledException("sensitive-delivery-detail")
            : new IOException("sensitive-delivery-detail"));

        var result = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test", recoveryUserId: RecoveryUserId);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, result.Error!.Code);
        Assert.IsFalse(result.Error.Message.Contains("sensitive-delivery-detail", StringComparison.Ordinal));
        AssertFailedRequestScope(fixture);
        Assert.AreEqual(3, fixture.Writes.Count);
        Assert.AreEqual(1, fixture.TransactionCount);
        if (outcome != "not-accepted")
        {
            Assert.AreEqual(1, fixture.Logger.Messages.Count);
            Assert.AreEqual($"Account challenge delivery threw; ChallengeId {fixture.Intents[0].ChallengeId}, Purpose {(byte)purpose}.",
                fixture.Logger.Messages[0]);
            CollectionAssert.AreEquivalent(new[] { "ChallengeId", "Purpose", "{OriginalFormat}" },
                fixture.Logger.Fields[0].Keys.ToArray());
            Assert.IsNull(fixture.Logger.Exceptions[0], "原始适配器异常可能含凭据，不得传入日志。");
            Assert.IsFalse(fixture.Logger.Messages[0].Contains("sensitive-delivery-detail", StringComparison.Ordinal));
            Assert.IsFalse(fixture.Logger.Messages[0].Contains("user@example.test", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification)]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery)]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification)]
    public async Task Late_delivery_exception_preserves_the_successful_replacement(
        IdentityAccountChallengePurpose purpose)
    {
        var fixture = new Fixture();
        var pending = fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test", recoveryUserId: RecoveryUserId);
        await fixture.FirstSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var replacement = await fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test", recoveryUserId: RecoveryUserId);
            Assert.IsTrue(replacement.IsSuccess);
            Assert.AreEqual(fixture.Intents[1].ChallengeId, replacement.Value!.ChallengeId);
        }
        finally
        {
            fixture.FirstDelivery.TrySetException(new IOException("sensitive-delivery-detail"));
        }
        var failed = await pending;
        Assert.IsFalse(failed.IsSuccess);
        AssertFailedRequestScope(fixture);
    }

    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification)]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery)]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification)]
    public async Task Caller_cancellation_is_not_converted_to_a_delivery_result(
        IdentityAccountChallengePurpose purpose)
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.Service.CreateAndDeliverAsync(purpose, "user@example.test", cancellation.Token, RecoveryUserId);
        await fixture.FirstSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        fixture.FirstDelivery.SetException(new OperationCanceledException(cancellation.Token));
        var exception = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => pending);
        Assert.AreEqual(cancellation.Token, exception.CancellationToken);
        Assert.AreEqual(0, fixture.Logger.Messages.Count);
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
        public RecordingLogger Logger { get; } = new();
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
            using var services = new ServiceCollection()
                .AddSingleton<ILogger<AccountChallengeService>>(Logger).BuildServiceProvider();
            Service = ActivatorUtilities.CreateInstance<AccountChallengeService>(services,
                query, command, transaction, delivery, clock, ids);
        }
    }

    private sealed class RecordingLogger : ILogger<AccountChallengeService>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public List<Dictionary<string, object?>> Fields { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
            Fields.Add(((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(item => item.Key, item => item.Value));
        }
    }
}
