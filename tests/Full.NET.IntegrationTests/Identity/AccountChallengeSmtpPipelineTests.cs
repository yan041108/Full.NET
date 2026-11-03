using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.Notifications;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>真实 HTTP、发布配置和 SMTP 适配器共用双库；只替换网络传输，不向外部邮箱发送测试邮件。</summary>
[TestClass]
public sealed class AccountChallengeSmtpPipelineTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Published_smtp_channel_preserves_locale_and_one_time_challenge_boundaries(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var transport = new ControlledTransport();
        var clock = new AdjustableClock();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["Notifications:Providers:Smtp:Enabled"] = "true" },
            configureTestServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ISmtpMailTransport>(transport));
                services.Replace(ServiceDescriptor.Singleton<INotificationSecretResolver>(new ControlledSecret()));
                services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
            });
        await factory.InitializeAsync();
        using var admin = factory.CreateClientForHost("localhost");
        using var anonymous = factory.CreateClientForHost("localhost");
        using var tenantClient = factory.CreateClientForHost("acme.localhost");
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(tenantClient);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var policies = scope.ServiceProvider.GetRequiredService<RegistrationPolicyService>();
            var current = await policies.GetAsync(default);
            Assert.IsTrue((await policies.UpdateAsync(new UpdateRegistrationPolicyRequest(true, current.Value!.Version, IdentityRegistrationMode.Open), default)).IsSuccess);
        }
        var registrationEmail = $"registration-{Guid.NewGuid():N}@example.test";
        using (var unavailable = await anonymous.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
            new SendRegistrationEmailChallengeRequest(registrationEmail, IdentityAccountChallengePurpose.RegistrationEmailVerification)))
            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, unavailable.StatusCode);
        Assert.AreEqual(0, transport.Count);
        var adminToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(admin, "admin");
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(admin, adminToken, "identity-controlled-smtp", "email.smtp",
            new { host = "smtp.controlled.test", port = 587, secureSocketMode = "starttls", username = "sender@example.test", fromAddress = "sender@example.test" },
            "env://CONTROLLED_SMTP_PASSWORD", default);
        await NotificationProfileBindingAssertions.PublishAndEnableAsync(admin, adminToken, profile, default);
        var wayId = await InvitedRegistrationAssertions.CreateRegistrationWayAsync(factory, tenant.Id, default);
        var invitedEmail = $"invited-{Guid.NewGuid():N}@example.test";
        var tenantToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(admin, adminToken, tenant.Id);
        using var invitationRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/identity/tenant-members/invitations")
        {
            Content = JsonContent.Create(new CreateTenantInvitationRequest(invitedEmail, "Member", RegistrationWayId: wayId)),
        };
        invitationRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var invitationResponse = await tenantClient.SendAsync(invitationRequest);
        Assert.AreEqual(HttpStatusCode.OK, invitationResponse.StatusCode);
        var createdInvitation = await invitationResponse.Content.ReadFromJsonAsync<CreateTenantInvitationResult>();
        Assert.IsNotNull(createdInvitation);
        var invitation = (InvitationId: createdInvitation.Invitation.Id, Token: createdInvitation.InvitationToken);
        var recoveryEmail = $"recovery-{Guid.NewGuid():N}@example.test";
        var recovery = await factory.CreateHostIdentityAsync("recover-" + Guid.NewGuid().ToString("N"), [], password: FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(recovery.UserId, recoveryEmail, "Recovery");

        foreach (var purpose in new[] { IdentityAccountChallengePurpose.RegistrationEmailVerification, IdentityAccountChallengePurpose.InvitationEmailVerification, IdentityAccountChallengePurpose.PasswordRecovery })
        {
            var email = purpose == IdentityAccountChallengePurpose.PasswordRecovery ? recoveryEmail
                : purpose == IdentityAccountChallengePurpose.InvitationEmailVerification ? invitedEmail : registrationEmail;
            var id = await RequestAsync(purpose, email, "zh-CN");
            Assert.IsTrue(transport.Last.Subject.Contains(purpose == IdentityAccountChallengePurpose.PasswordRecovery ? "找回密码"
                : purpose == IdentityAccountChallengePurpose.InvitationEmailVerification ? "邀请验证" : "注册验证", StringComparison.Ordinal));
            Assert.IsTrue(await ConsumeAsync(id, purpose, email, Code(transport.Last)));
            Assert.IsFalse(await ConsumeAsync(id, purpose, email, Code(transport.Last)));
        }
        var englishEmail = $"english-{Guid.NewGuid():N}@example.test";
        var englishId = await RequestAsync(IdentityAccountChallengePurpose.RegistrationEmailVerification, englishEmail, "en-US");
        Assert.AreEqual("Full.NET registration verification", transport.Last.Subject);
        Assert.IsTrue(transport.Last.Body.Contains("Your verification code", StringComparison.Ordinal));
        Assert.IsTrue(await ConsumeAsync(englishId, IdentityAccountChallengePurpose.RegistrationEmailVerification, englishEmail, Code(transport.Last)));

        transport.Unknown = true;
        var retryEmail = $"retry-{Guid.NewGuid():N}@example.test";
        var unknownId = await RequestAsync(IdentityAccountChallengePurpose.RegistrationEmailVerification, retryEmail, "zh-CN", expectedSuccess: false);
        var unknownCode = Code(transport.Last);
        Assert.IsFalse(await ConsumeAsync(unknownId, IdentityAccountChallengePurpose.RegistrationEmailVerification, retryEmail, unknownCode));
        transport.Unknown = false;
        var resentId = await RequestAsync(IdentityAccountChallengePurpose.RegistrationEmailVerification, retryEmail, "zh-CN");
        Assert.AreNotEqual(unknownId, resentId);
        Assert.IsFalse(await ConsumeAsync(unknownId, IdentityAccountChallengePurpose.RegistrationEmailVerification, retryEmail, unknownCode));
        Assert.IsTrue(await ConsumeAsync(resentId, IdentityAccountChallengePurpose.RegistrationEmailVerification, retryEmail, Code(transport.Last)));
        var expiredId = await RequestAsync(IdentityAccountChallengePurpose.RegistrationEmailVerification, retryEmail, "en-US");
        var expiredCode = Code(transport.Last);
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.IsFalse(await ConsumeAsync(expiredId, IdentityAccountChallengePurpose.RegistrationEmailVerification, retryEmail, expiredCode));

        async Task<Guid> RequestAsync(IdentityAccountChallengePurpose purpose, string email, string locale, bool expectedSuccess = true)
        {
            var previous = transport.Count;
            using var request = new HttpRequestMessage(HttpMethod.Post, purpose == IdentityAccountChallengePurpose.PasswordRecovery
                ? "/api/v1/auth/recover-password" : "/api/v1/auth/register/email-challenge");
            request.Headers.AcceptLanguage.ParseAdd(locale);
            request.Content = purpose == IdentityAccountChallengePurpose.PasswordRecovery
                ? JsonContent.Create(new RequestPasswordRecoveryRequest(email))
                : JsonContent.Create(new SendRegistrationEmailChallengeRequest(email, purpose,
                    purpose == IdentityAccountChallengePurpose.InvitationEmailVerification ? invitation.InvitationId : null,
                    purpose == IdentityAccountChallengePurpose.InvitationEmailVerification ? invitation.Token : null));
            using var response = await anonymous.SendAsync(request);
            Assert.AreEqual(expectedSuccess ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, response.StatusCode);
            Assert.AreEqual(previous + 1, transport.Count);
            if (!expectedSuccess)
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, problem.RootElement.GetProperty("code").GetString());
                return Guid.ParseExact(transport.Last.IdempotencyKey.Split(':')[2], "N");
            }
            var result = await response.Content.ReadFromJsonAsync<AccountChallengeAcceptedResponse>();
            Assert.IsNotNull(result);
            return result.ChallengeId;
        }
        async Task<bool> ConsumeAsync(Guid id, IdentityAccountChallengePurpose purpose, string email, string code)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            return (await scope.ServiceProvider.GetRequiredService<AccountChallengeService>().ConsumeAsync(id, purpose, email, code)).IsSuccess;
        }
    }

    private static string Code(SmtpSendCommand command)
    {
        var match = Regex.Match(command.Body, @"(?<!\d)\d{6}(?!\d)");
        Assert.IsTrue(match.Success, "受控邮件必须包含一次性验证码；失败诊断不得输出正文。");
        return match.Value;
    }
    private sealed class ControlledTransport : ISmtpMailTransport
    {
        internal int Count;
        internal bool Unknown;
        internal SmtpSendCommand Last = null!;
        public ValueTask<string> SendAsync(SmtpSendCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Count++;
            Last = command;
            return Unknown ? ValueTask.FromException<string>(new SmtpTransportException(SmtpTransportFailureKind.Unknown))
                : ValueTask.FromResult("controlled-message-id");
        }
    }
    private sealed class ControlledSecret : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string providerTypeKey, string? reference, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(providerTypeKey == "email.smtp" && reference == "env://CONTROLLED_SMTP_PASSWORD" ? "controlled-password" : null);
    }
    private sealed class AdjustableClock : IClock
    {
        private TimeSpan offset;
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow.Add(offset);
        internal void Advance(TimeSpan value) => offset += value;
    }
}
