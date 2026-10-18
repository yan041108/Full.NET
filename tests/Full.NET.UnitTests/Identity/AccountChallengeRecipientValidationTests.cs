using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Notifications.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class AccountChallengeRecipientValidationTests
{
    [TestMethod]
    [DataRow("@example.com")]
    [DataRow("user@")]
    [DataRow("user@@example.com")]
    [DataRow("User <user@example.com>")]
    [DataRow("user@example.com,other@example.com")]
    [DataRow("user@example.com;other@example.com")]
    [DataRow("user(comment)@example.com")]
    [DataRow("user@example.com\r\nBcc: other@example.com")]
    [DataRow("\nuser@example.com")]
    [DataRow("user@example.com\t")]
    public async Task Invalid_single_recipient_never_creates_or_delivers_a_challenge(string email)
    {
        var query = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>();
        transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Func<CancellationToken, Task<bool>>>(0)(call.ArgAt<CancellationToken>(1)));
        var delivery = Substitute.For<IIdentityChallengeDeliveryPort>();
        delivery.SendAsync(Arg.Any<IdentityChallengeDeliveryIntent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<bool>.Success(true)));
        var clock = Substitute.For<IClock>();clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ids = Substitute.For<IIdGenerator>();ids.NewId().Returns(_ => Guid.CreateVersion7());
        var service = new AccountChallengeService(query, commands, transaction, delivery, clock, ids);
        var result = await service.CreateAndDeliverAsync(IdentityAccountChallengePurpose.RegistrationEmailVerification, email);
        Assert.IsFalse(result.IsSuccess, "非单个规范邮箱不得提交挑战或调用外部渠道。");
        Assert.IsNull(AccountChallengeService.NormalizeEmail(email));
        await commands.DidNotReceive().ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await delivery.DidNotReceive().SendAsync(Arg.Any<IdentityChallengeDeliveryIntent>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(" User@Example.com ", "user@example.com")]
    [DataRow("user+tag@example.test", "user+tag@example.test")]
    public void Valid_single_recipient_keeps_existing_canonical_form(string email, string expected) =>
        Assert.AreEqual(expected, AccountChallengeService.NormalizeEmail(email));
}
