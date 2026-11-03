using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Features.SendIdentityChallenge;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>实际 SMTP 适配器收到协商语言的账号邮件，展示内容不改变验证码及 UTC 截止时刻。</summary>
[TestClass]
public sealed class IdentityChallengeMailTests
{
    private static readonly Guid ChallengeId = Guid.Parse("01900000-0000-7000-8000-000000000001");

    [TestMethod]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification, "zh-CN", "Full.NET 注册验证")]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification, "zh-CN", "Full.NET 邀请验证")]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery, "zh-CN", "Full.NET 找回密码")]
    [DataRow(IdentityAccountChallengePurpose.RegistrationEmailVerification, "en-US", "Full.NET registration verification")]
    [DataRow(IdentityAccountChallengePurpose.InvitationEmailVerification, "en-US", "Full.NET invitation verification")]
    [DataRow(IdentityAccountChallengePurpose.PasswordRecovery, "en-US", "Full.NET password recovery")]
    public async Task Purpose_and_locale_select_mail_without_changing_security_values(IdentityAccountChallengePurpose purpose, string locale, string subject)
    {
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationProviderProfileVersionRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "email.smtp", "1.0.0",
                """{"host":"smtp.controlled.test","port":587,"secureSocketMode":"starttls","username":"sender@example.test","fromAddress":"sender@example.test"}""",
                "env://CONTROLLED_SMTP_PASSWORD", "hash", Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        var transport = new ControlledTransport();
        var port = new IdentityChallengeDeliveryPort(query, [new SmtpNotificationProviderAdapter(new ControlledSecret(), transport)],
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        var intent = Intent(purpose, locale);
        Assert.IsTrue((await port.SendAsync(intent)).IsSuccess);
        var captured = transport.Last;
        Assert.IsNotNull(captured);
        Assert.AreEqual(subject, captured.Subject);
        StringAssert.Contains(captured.Body, "654321");
        StringAssert.Contains(captured.Body, "2030-01-01 01:02:03 UTC");
        StringAssert.Contains(captured.Body, locale == "en-US" ? "Your verification code" : "您的验证码");
        Assert.AreEqual(intent.IdempotencyKey, captured.IdempotencyKey);
        Assert.AreEqual(intent.NormalizedEmail, captured.RecipientAddress);
    }

    [TestMethod]
    public void Intent_diagnostic_text_does_not_disclose_code_or_recipient()
    {
        var text = Intent(IdentityAccountChallengePurpose.RegistrationEmailVerification, "zh-CN").ToString();
        Assert.DoesNotContain("654321", text);
        Assert.DoesNotContain("recipient@example.test", text);
        StringAssert.Contains(text, "[redacted]");
    }

    private static IdentityChallengeDeliveryIntent Intent(IdentityAccountChallengePurpose purpose, string locale) =>
        new(ChallengeId, purpose, "recipient@example.test", "654321", new DateTimeOffset(2030, 1, 1, 1, 2, 3, TimeSpan.Zero), "controlled-idempotency", locale);

    private sealed class ControlledSecret : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string providerTypeKey, string? reference, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("controlled-password");
    }
    private sealed class ControlledTransport : ISmtpMailTransport
    {
        internal SmtpSendCommand? Last;
        public ValueTask<string> SendAsync(SmtpSendCommand command, CancellationToken cancellationToken)
        {
            Last = command;
            return ValueTask.FromResult("controlled-message-id");
        }
    }
}
