using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.SendIdentityChallenge;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>投递边界使用真实 SMTP 适配器，仅替换数据库和网络，验证外发前的安全约束。</summary>
[TestClass]
public sealed class IdentityChallengeDeliveryPortTests
{
    [TestMethod]
    [DataRow("unknown-purpose")]
    [DataRow("empty-id")]
    [DataRow("null-email")]
    [DataRow("empty-email")]
    [DataRow("invalid-email")]
    [DataRow("display-name")]
    [DataRow("comment-email")]
    [DataRow("multiple-email")]
    [DataRow("control-email")]
    [DataRow("padded-email")]
    [DataRow("null-credential")]
    [DataRow("empty-credential")]
    [DataRow("control-credential")]
    [DataRow("null-key")]
    [DataRow("empty-key")]
    [DataRow("control-key")]
    public async Task Invalid_intent_is_rejected_before_profile_lookup(string invalidField)
    {
        var fixture = new Fixture();
        var intent = fixture.Intent;
        intent = invalidField switch
        {
            "unknown-purpose" => intent with { Purpose = (IdentityAccountChallengePurpose)255 },
            "empty-id" => intent with { ChallengeId = Guid.Empty },
            "null-email" => intent with { NormalizedEmail = null! },
            "empty-email" => intent with { NormalizedEmail = " " },
            "invalid-email" => intent with { NormalizedEmail = "invalid" },
            "display-name" => intent with { NormalizedEmail = "Receiver <receiver@example.test>" },
            "comment-email" => intent with { NormalizedEmail = "receiver(comment)@example.test" },
            "multiple-email" => intent with { NormalizedEmail = "receiver@example.test,other@example.test" },
            "control-email" => intent with { NormalizedEmail = "receiver@example.test\r\n" },
            "padded-email" => intent with { NormalizedEmail = " receiver@example.test " },
            "null-credential" => intent with { Credential = null! },
            "empty-credential" => intent with { Credential = " " },
            "control-credential" => intent with { Credential = "123456\r\n" },
            "null-key" => intent with { IdempotencyKey = null! },
            "empty-key" => intent with { IdempotencyKey = " " },
            "control-key" => intent with { IdempotencyKey = "key\n" },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField)),
        };

        var result = await fixture.Port.SendAsync(intent);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, result.Error!.Code);
        Assert.AreEqual(0, fixture.Queries.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Transport.Sends);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    public async Task Expired_intent_is_rejected_before_profile_lookup(int expiryOffsetSeconds)
    {
        var fixture = new Fixture();
        var result = await fixture.Port.SendAsync(fixture.Intent with
        {
            ExpiresAtUtc = fixture.Clock.UtcNow.AddSeconds(expiryOffsetSeconds),
        });

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, fixture.Queries.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Transport.Sends);
    }

    /// <summary>配置读取耗时可以越过有效期，必须在开始 SMTP 投递前再次检查。</summary>
    [TestMethod]
    public async Task Intent_expiring_during_profile_lookup_is_not_sent()
    {
        var fixture = new Fixture();
        fixture.Queries.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fixture.Clock.UtcNow = fixture.Intent.ExpiresAtUtc;
                return fixture.Profile;
            });

        var result = await fixture.Port.SendAsync(fixture.Intent);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, fixture.Queries.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Transport.Sends);
    }

    [TestMethod]
    public async Task Cancellation_stops_before_profile_lookup()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.Port.SendAsync(fixture.Intent, cancellation.Token));

        Assert.AreEqual(0, fixture.Queries.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Transport.Sends);
    }

    [TestMethod]
    public void Intent_diagnostics_do_not_expose_credential_recipient_or_key()
    {
        var intent = new Fixture().Intent;
        var text = $"{intent with { Credential = "private-one-time-token", IdempotencyKey = "private-key" }}";

        Assert.IsFalse(text.Contains("private-one-time-token", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains(intent.NormalizedEmail, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("private-key", StringComparison.Ordinal));
        StringAssert.Contains(text, intent.ChallengeId.ToString());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.MySql, IdentityAccountChallengePurpose.RegistrationEmailVerification, "Full.NET registration verification")]
    [DataRow(DatabaseProvider.SqlServer, IdentityAccountChallengePurpose.RegistrationEmailVerification, "Full.NET registration verification")]
    [DataRow(DatabaseProvider.MySql, IdentityAccountChallengePurpose.PasswordRecovery, "Full.NET password recovery")]
    [DataRow(DatabaseProvider.SqlServer, IdentityAccountChallengePurpose.PasswordRecovery, "Full.NET password recovery")]
    [DataRow(DatabaseProvider.MySql, IdentityAccountChallengePurpose.InvitationEmailVerification, "Full.NET invitation verification")]
    [DataRow(DatabaseProvider.SqlServer, IdentityAccountChallengePurpose.InvitationEmailVerification, "Full.NET invitation verification")]
    public async Task Supported_purposes_keep_recipient_credential_and_provider_sql(
        DatabaseProvider provider, IdentityAccountChallengePurpose purpose, string subject)
    {
        var fixture = new Fixture(provider);
        var intent = fixture.Intent with { Purpose = purpose };

        var result = await fixture.Port.SendAsync(intent);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Value);
        Assert.AreEqual(1, fixture.Transport.Sends);
        Assert.AreEqual(subject, fixture.Transport.Command!.Subject);
        Assert.AreEqual(intent.NormalizedEmail, fixture.Transport.Command.RecipientAddress);
        Assert.AreEqual(intent.IdempotencyKey, fixture.Transport.Command.IdempotencyKey);
        StringAssert.Contains(fixture.Transport.Command.Body, intent.Credential);
        await fixture.Queries.Received(1).QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
            provider == DatabaseProvider.MySql
                ? IdentityChallengeDeliverySql.FindFirstHostSmtpProfileVersionMySql
                : IdentityChallengeDeliverySql.FindFirstHostSmtpProfileVersionSqlServer,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        public Fixture(DatabaseProvider provider = DatabaseProvider.MySql)
        {
            Queries.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                    Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(Profile);
            Port = new IdentityChallengeDeliveryPort(Queries,
                [new SmtpNotificationProviderAdapter(new SecretResolver(), Transport)],
                Options.Create(new DatabaseOptions { Provider = provider }), Clock);
            Intent = new IdentityChallengeDeliveryIntent(Guid.NewGuid(),
                IdentityAccountChallengePurpose.RegistrationEmailVerification,
                "receiver@example.test", "123456", Clock.UtcNow.AddMinutes(15), "opaque-delivery-key");
        }

        public IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        public MutableClock Clock { get; } = new(DateTimeOffset.UtcNow);
        public RecordingTransport Transport { get; } = new();
        public IdentityChallengeDeliveryPort Port { get; }
        public IdentityChallengeDeliveryIntent Intent { get; }
        public NotificationProviderProfileVersionRecord Profile { get; } = new(
            Guid.NewGuid(), Guid.NewGuid(), 1, "email.smtp", "1.0.0",
            "{\"fromAddress\":\"sender@example.test\",\"host\":\"smtp.example.test\",\"port\":465,\"secureSocketMode\":\"ssl_on_connect\",\"username\":\"sender@example.test\"}",
            "env://test", "test-hash", Guid.NewGuid(), DateTimeOffset.UtcNow);
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    private sealed class SecretResolver : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string providerTypeKey, string? secretReference, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("test-password");
    }

    private sealed class RecordingTransport : ISmtpMailTransport
    {
        public int Sends { get; private set; }
        public SmtpSendCommand? Command { get; private set; }

        public ValueTask<string> SendAsync(SmtpSendCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sends++;
            Command = command;
            return ValueTask.FromResult("test-message-id");
        }
    }
}
