using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Localization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

/// <summary>投递 Port 返回 false 也属于未发送，不能留下可用挑战响应。</summary>
[TestClass]
public sealed class AccountChallengeDeliveryContractTests
{
    [TestMethod]
    public async Task False_delivery_revokes_its_challenge_after_local_commit()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var delivery = Substitute.For<IIdentityChallengeDeliveryPort>();
        delivery.SendAsync(Arg.Any<IdentityChallengeDeliveryIntent>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsFalse(coordinator.HasTransaction);
            return Result<bool>.Success(false);
        });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(Guid.CreateVersion7());
        var locale = Substitute.For<ILocaleContext>();
        locale.CurrentLocale.Returns(LocaleCatalog.Chinese);
        var service = new AccountChallengeService(Substitute.For<IQueryExecutor>(), commands,
            new DapperCommandTransaction(coordinator), delivery, clock, ids, locale);
        var result = await service.CreateAndDeliverAsync(IdentityAccountChallengePurpose.RegistrationEmailVerification, "recipient@example.test");
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, result.Error!.Code);
        Assert.AreEqual(1, coordinator.CommitCount);
        await commands.Received(1).ExecuteAsync(AccountChallengeSql.InvalidateById, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
}
