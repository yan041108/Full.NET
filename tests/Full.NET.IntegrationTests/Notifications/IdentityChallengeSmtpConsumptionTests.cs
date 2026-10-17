using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Features.RegistrationInvitations;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features.SendIdentityChallenge;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>通过正式挑战 Port、真实 SMTP 和公开消费入口验证三种用途，不从投递意图替身取得验证码。</summary>
[TestClass]
public sealed class IdentityChallengeSmtpConsumptionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Real_mail_challenges_are_consumed_once_or_revoked_with_sql_server(bool startTls) =>
        VerifyAsync(DatabaseProvider.SqlServer, startTls);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Real_mail_challenges_are_consumed_once_or_revoked_with_mysql(bool startTls) =>
        VerifyAsync(DatabaseProvider.MySql, startTls);

    [TestMethod]
    public void Capture_observes_logs_when_the_downstream_sink_is_disabled()
    {
        var entries = new ConcurrentQueue<string>();
        using var factory = new CapturingLoggerFactory(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance, entries);
        var logger = factory.CreateLogger(typeof(AccountChallengeService).FullName!);
        Assert.IsTrue(logger.IsEnabled(LogLevel.Information));
        Assert.IsTrue(logger.IsEnabled(LogLevel.Warning));
        Assert.IsFalse(logger.IsEnabled(LogLevel.None));
        logger.LogInformation("SMTP capture disabled-sink probe.");
        var generatedWarning = LoggerMessage.Define(LogLevel.Warning, new EventId(4532), "SMTP capture generated warning.");
        generatedWarning(logger, null);
        CollectionAssert.AreEqual(new[] { "SMTP capture disabled-sink probe.", "SMTP capture generated warning." }, entries.ToArray());
    }

    private async Task VerifyAsync(DatabaseProvider provider, bool startTls)
    {
        await using var inbox = new ControlledSmtpInbox(startTls);
        using var logs = new ChallengeLogCapture();
        var connection = provider == DatabaseProvider.MySql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection,
            new Dictionary<string, string?> { ["Notifications:Providers:Smtp:Enabled"] = "true" },
            configureTestServices: services =>
            {
                // 只替换测试根信任和测试秘密；正式 Port、适配器、配置目录和所有 HTTP 路径保持装配。
                services.RemoveAll<ISmtpMailTransport>();
                services.AddSingleton<ISmtpMailTransport>(inbox.CreateTransport());
                services.RemoveAll<INotificationSecretResolver>();
                services.AddSingleton<INotificationSecretResolver>(new ControlledSmtpInbox.ProtocolSecretResolver());
                var registration = services.Last(descriptor => !descriptor.IsKeyedService
                    && descriptor.ServiceType == typeof(ILoggerFactory));
                Assert.AreEqual(ServiceLifetime.Singleton, registration.Lifetime);
                services.Remove(registration);
                services.AddSingleton<ILoggerFactory>(provider => new CapturingLoggerFactory(
                    registration.ImplementationInstance as ILoggerFactory
                    ?? registration.ImplementationFactory?.Invoke(provider) as ILoggerFactory
                    ?? (ILoggerFactory)ActivatorUtilities.CreateInstance(provider, registration.ImplementationType!), logs.Entries));
            });
        await factory.InitializeAsync(TestContext.CancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await AccountLifecycleAssertions.LoginAsHostAdminAsync(client, TestContext.CancellationToken);
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(client, adminToken,
            $"smtp-{Guid.NewGuid():N}"[..20], "email.smtp", inbox.Configuration, "test://smtp", TestContext.CancellationToken);
        await NotificationProfileBindingAssertions.PublishAndEnableAsync(client, adminToken, profile, TestContext.CancellationToken);
        var (tenantId, wayId) = await CreateRegistrationWayAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.IsInstanceOfType<IdentityChallengeDeliveryPort>(scope.ServiceProvider.GetRequiredService<IIdentityChallengeDeliveryPort>());
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var policy = scope.ServiceProvider.GetRequiredService<RegistrationPolicyService>();
        var invitations = scope.ServiceProvider.GetRequiredService<RegistrationInvitationService>();
        // 正向探针来自挑战服务使用的同类 DI Logger；禁止以未接通的空收集器证明脱敏。
        scope.ServiceProvider.GetRequiredService<ILogger<AccountChallengeService>>().LogInformation("SMTP challenge log capture ready.");
        Assert.IsTrue(logs.Entries.Any(entry => entry.Contains("SMTP challenge log capture ready.", StringComparison.Ordinal)));
        foreach (var purpose in new[]
        {
            IdentityAccountChallengePurpose.RegistrationEmailVerification,
            IdentityAccountChallengePurpose.InvitationEmailVerification,
            IdentityAccountChallengePurpose.PasswordRecovery,
        })
        foreach (var outcome in new[] { "accepted", "quit_lost", "ack_lost", "authentication_rejection" })
        {
            var email = $"smtp-{Guid.NewGuid():N}@example.test";
            var mode = purpose == IdentityAccountChallengePurpose.InvitationEmailVerification
                ? IdentityRegistrationMode.InvitationOnly : IdentityRegistrationMode.Open;
            Guid? invitationId = null;
            string? invitationToken = null;
            context.SetHost();
            try
            {
                var current = await policy.GetAsync(TestContext.CancellationToken);
                Assert.IsTrue(current.IsSuccess);
                Assert.IsTrue((await policy.UpdateAsync(new UpdateRegistrationPolicyRequest(true, current.Value!.Version, mode),
                    TestContext.CancellationToken)).IsSuccess);
                if (purpose == IdentityAccountChallengePurpose.InvitationEmailVerification)
                {
                    var invitation = await invitations.CreateAsync(tenantId, email, wayId, TestContext.CancellationToken);
                    Assert.IsTrue(invitation.IsSuccess);
                    (invitationId, invitationToken) = invitation.Value;
                }
            }
            finally { context.Clear(); }
            if (purpose == IdentityAccountChallengePurpose.PasswordRecovery)
                await AccountLifecycleAssertions.CreateHostUserWithEmailAsync(client, adminToken, email, TestContext.CancellationToken);
            var originalUser = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail,
                IdentitySqlParameters.Create(("Email", email)), TestContext.CancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var received = inbox.ReceiveAsync(outcome, timeout.Token);
            using var response = purpose == IdentityAccountChallengePurpose.PasswordRecovery
                ? await client.PostAsJsonAsync("/api/v1/auth/recover-password", new RequestPasswordRecoveryRequest(email), timeout.Token)
                : await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
                    new SendRegistrationEmailChallengeRequest(email, purpose, invitationId, invitationToken), timeout.Token);
            var observation = await received;
            var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
            var accepted = outcome is "accepted" or "quit_lost";
            Assert.AreEqual(accepted || purpose == IdentityAccountChallengePurpose.PasswordRecovery
                ? HttpStatusCode.OK : HttpStatusCode.UnprocessableEntity, response.StatusCode, $"{purpose}/{outcome}");
            var record = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(new SqlStatement(
                "integration.smtp_challenge_by_email", """
                SELECT ChallengeId, Purpose, NormalizedEmail, CredentialHash, ExpiresAtUtc,
                       ConsumedAtUtc, AttemptCount, MaxAttempts, Version, CreatedAtUtc,
                       DeliveryStateKey, DeliveryCompletedAtUtc, DeliveryReconciledAtUtc
                FROM fn_identity_account_challenge WHERE NormalizedEmail = @Email AND Purpose = @Purpose
                """, SqlDataScope.Global), new { Email = email, Purpose = (byte)purpose }, timeout.Token);
            Assert.IsNotNull(record);
            Assert.AreEqual(!accepted, record.ConsumedAtUtc.HasValue);
            Assert.AreEqual(accepted ? 1 : 2, record.Version);
            Assert.AreEqual(accepted ? "accepted" : outcome == "ack_lost" ? "unknown" : "rejected", record.DeliveryStateKey);
            Assert.IsNotNull(record.DeliveryCompletedAtUtc);
            Assert.AreEqual(!accepted, record.DeliveryReconciledAtUtc.HasValue);
            if (response.IsSuccessStatusCode)
            {
                using var body = JsonDocument.Parse(responseText);
                CollectionAssert.AreEquivalent(new[] { "challengeId", "expiresAtUtc" },
                    body.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
                var responseId = body.RootElement.GetProperty("challengeId").GetGuid();
                if (accepted) Assert.AreEqual(record.ChallengeId, responseId);
                else
                {
                    Assert.AreNotEqual(record.ChallengeId, responseId);
                    Assert.IsNull(await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                        IdentitySqlParameters.Create(("ChallengeId", responseId)), timeout.Token));
                }
            }
            else
            {
                using var problem = JsonDocument.Parse(responseText);
                Assert.AreEqual(IdentityErrorCodes.AccountChallengeDeliveryFailed, problem.RootElement.GetProperty("code").GetString());
            }
            if (outcome == "authentication_rejection")
            {
                Assert.IsFalse(observation.Authenticated);
                Assert.IsNull(observation.Message);
                await AssertNoBusinessMutationAsync();
                Assert.IsFalse(logs.Entries.Any(entry => entry.Contains("protocol-password", StringComparison.Ordinal)));
                continue;
            }
            Assert.IsTrue(observation.Authenticated);
            Assert.IsNotNull(observation.Message);
            Assert.AreEqual(email, observation.Message.To.Mailboxes.Single().Address);
            Assert.AreEqual(purpose switch
            {
                IdentityAccountChallengePurpose.PasswordRecovery => "Full.NET password recovery",
                IdentityAccountChallengePurpose.InvitationEmailVerification => "Full.NET invitation verification",
                _ => "Full.NET registration verification",
            }, observation.Message.Subject);
            var bodyText = observation.Message.TextBody;
            Assert.IsNotNull(bodyText);
            var match = Regex.Match(bodyText, @"^Your verification code is ([0-9]{6})\. It expires at ");
            Assert.IsTrue(match.Success, "必须从实际收到的 MIME 正文取得凭据。");
            var credential = match.Groups[1].Value;
            Assert.IsFalse(record.CredentialHash == credential);
            Assert.IsFalse(Regex.IsMatch(responseText,
                $@"(?<![A-Za-z0-9]){Regex.Escape(credential)}(?![A-Za-z0-9])"));

            var register = new RegisterAccountRequest(email, "SMTP Verified", "FullNet!2026SmtpVerified",
                record.ChallengeId, credential, wayId, invitationId, invitationToken);
            var recover = new ConfirmPasswordRecoveryRequest(record.ChallengeId, credential, "FullNet!2026SmtpRecovered");
            if (accepted)
            {
                // 用同一目标邮箱单独核对用途边界，避免公开路由先被已有账号或邀请政策拒绝而产生假阳性。
                var wrongPurpose = await scope.ServiceProvider.GetRequiredService<AccountChallengeService>().ValidateAsync(
                    record.ChallengeId, purpose == IdentityAccountChallengePurpose.RegistrationEmailVerification
                        ? IdentityAccountChallengePurpose.InvitationEmailVerification
                        : IdentityAccountChallengePurpose.RegistrationEmailVerification,
                    email, credential, timeout.Token);
                Assert.IsFalse(wrongPurpose.IsSuccess);
                Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, wrongPurpose.Error!.Code);
                var before = await ReadAsync();
                Assert.IsNotNull(before);
                Assert.IsNull(before.ConsumedAtUtc);
            }
            using var consume = purpose == IdentityAccountChallengePurpose.PasswordRecovery
                ? await client.PostAsJsonAsync("/api/v1/auth/recover-password/confirm", recover, timeout.Token)
                : await client.PostAsJsonAsync("/api/v1/auth/register", register, timeout.Token);
            Assert.AreEqual(accepted
                ? purpose == IdentityAccountChallengePurpose.PasswordRecovery ? HttpStatusCode.NoContent : HttpStatusCode.OK
                : HttpStatusCode.BadRequest, consume.StatusCode, $"consume {purpose}/{outcome}");
            var consumed = await ReadAsync();
            Assert.IsNotNull(consumed);
            Assert.IsNotNull(consumed.ConsumedAtUtc);
            if (!accepted)
            {
                using var problem = JsonDocument.Parse(await consume.Content.ReadAsStringAsync(timeout.Token));
                Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, problem.RootElement.GetProperty("code").GetString());
                await AssertNoBusinessMutationAsync();
            }
            else
            {
                using var replay = purpose == IdentityAccountChallengePurpose.PasswordRecovery
                    ? await client.PostAsJsonAsync("/api/v1/auth/recover-password/confirm", recover, timeout.Token)
                    : await client.PostAsJsonAsync("/api/v1/auth/register", register, timeout.Token);
                Assert.IsFalse(replay.IsSuccessStatusCode, "公开流程不得重复成功消费收到的验证码。");
                Assert.AreEqual(consumed, await ReadAsync(), "重放不得再次修改已消费挑战。");
                var currentUser = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail,
                    IdentitySqlParameters.Create(("Email", email)), timeout.Token);
                Assert.IsNotNull(currentUser);
                if (purpose == IdentityAccountChallengePurpose.PasswordRecovery)
                {
                    Assert.IsNotNull(originalUser);
                    Assert.IsFalse(originalUser.PasswordHash == currentUser.PasswordHash);
                    Assert.IsFalse(originalUser.SecurityStamp == currentUser.SecurityStamp);
                    // 独立客户端检验新密码真实可登录，避免其 Cookie 影响下一轮匿名挑战请求。
                    using var loginClient = factory.CreateClientForHost("localhost");
                    foreach (var password in new[] { FullNetApiFactory.TestPassword, recover.NewPassword })
                    {
                        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                        { Content = JsonContent.Create(new LoginRequest(currentUser.Username, password)) };
                        login.Headers.Add("Origin", "http://localhost");
                        using var loggedIn = await loginClient.SendAsync(login, timeout.Token);
                        Assert.AreEqual(password == recover.NewPassword, loggedIn.IsSuccessStatusCode);
                    }
                }
                if (invitationId.HasValue)
                {
                    var invitation = await query.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(RegistrationInvitationSql.FindById,
                        IdentitySqlParameters.Create(("InvitationId", invitationId.Value)), timeout.Token);
                    Assert.IsNotNull(invitation);
                    Assert.AreEqual((byte)IdentityRegistrationInvitationStatus.AccountCreatedPendingOnboarding, invitation.Status);
                    Assert.AreEqual(currentUser.Id, invitation.BoundUserId);
                    Assert.IsNotNull(invitation.ConsumedAtUtc);
                }
                var consumedAgain = await scope.ServiceProvider.GetRequiredService<AccountChallengeService>().ValidateAsync(
                    record.ChallengeId, purpose, email, credential, timeout.Token,
                    recoveryUserId: currentUser.Id, recoverySecurityStamp: currentUser.SecurityStamp);
                Assert.IsFalse(consumedAgain.IsSuccess);
                Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, consumedAgain.Error!.Code);
            }
            Assert.IsFalse(logs.Entries.Any(entry => Regex.IsMatch(entry,
                $@"(?<![A-Za-z0-9]){Regex.Escape(credential)}(?![A-Za-z0-9])")), "日志不得回显实际邮件验证码。");
            Assert.IsFalse(logs.Entries.Any(entry => entry.Contains("protocol-password", StringComparison.Ordinal)));

            Task<AccountChallengeRecord?> ReadAsync() => query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
                AccountChallengeSql.FindById, IdentitySqlParameters.Create(("ChallengeId", record.ChallengeId)), timeout.Token);

            async Task AssertNoBusinessMutationAsync()
            {
                var user = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(AccountLifecycleSql.FindUserByProfileEmail,
                    IdentitySqlParameters.Create(("Email", email)), timeout.Token);
                if (originalUser is null) Assert.IsNull(user, "失败投递不得创建注册账号。");
                else
                {
                    Assert.IsNotNull(user);
                    Assert.IsTrue(originalUser.PasswordHash == user.PasswordHash);
                    Assert.IsTrue(originalUser.SecurityStamp == user.SecurityStamp);
                }
                if (!invitationId.HasValue) return;
                var invitation = await query.QuerySingleOrDefaultAsync<RegistrationInvitationRecord>(RegistrationInvitationSql.FindById,
                    IdentitySqlParameters.Create(("InvitationId", invitationId.Value)), timeout.Token);
                Assert.IsNotNull(invitation);
                Assert.AreEqual((byte)IdentityRegistrationInvitationStatus.Pending, invitation.Status);
                Assert.IsNull(invitation.ConsumedAtUtc);
                Assert.IsNull(invitation.BoundUserId);
            }
        }
    }

    private async Task<(Guid TenantId, Guid WayId)> CreateRegistrationWayAsync(FullNetApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var tenantId = await query.QuerySingleOrDefaultAsync<Guid>(new SqlStatement("integration.smtp_target_tenant",
            "SELECT Id FROM fn_tenancy_tenant WHERE Identifier = @Identifier", SqlDataScope.Global), new { Identifier = "acme" }, TestContext.CancellationToken);
        Assert.AreNotEqual(Guid.Empty, tenantId);
        var roleId = Guid.CreateVersion7();
        var unitId = Guid.CreateVersion7();
        var wayId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        try
        {
            // 仅构造本用例的注册目标；不依赖开发 Overlay，也不修改正式 Baseline。
            context.SetHost();
            await command.ExecuteAsync(IdentitySql.InsertRole, new
            {
                Id = roleId, TenantId = (Guid?)tenantId, ScopeKey = $"tenant:{tenantId:N}", Code = $"smtp-{roleId:N}",
                Name = "SMTP Registration", IsSystem = false, IsActive = true, IsSuperAdministrator = false,
                DataScopeKind = "all", CreatedAtUtc = now, Version = 1,
            }, TestContext.CancellationToken);
            context.SetTenant(new TenantContext(tenantId, "acme", "Acme Corporation"));
            await command.ExecuteAsync(new SqlStatement("integration.smtp_registration_unit", """
                INSERT INTO fn_organization_unit
                    (Id, TenantId, ParentId, Code, Name, DisplayOrder, IsActive, CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES (@Id, @TenantId, NULL, @Code, @Name, 0, 1, @CreatedAtUtc, NULL, 1)
                """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
                new { Id = unitId, Code = $"smtp-{unitId:N}", Name = "SMTP Registration", CreatedAtUtc = now }, TestContext.CancellationToken);
            context.SetHost();
            await command.ExecuteAsync(RegistrationWaySql.Insert, new RegistrationWayRecord
            {
                Id = wayId, TenantId = tenantId, Name = "SMTP Registration", Code = $"smtp-{wayId:N}", IsEnabled = true,
                RoleId = roleId, OrganizationUnitId = unitId, SortOrder = 0, CreatedAtUtc = now, Version = 1,
            }, TestContext.CancellationToken);
            return (tenantId, wayId);
        }
        finally { context.Clear(); }
    }

    private sealed class ChallengeLogCapture : IDisposable
    {
        public ConcurrentQueue<string> Entries { get; } = new();
        public void Dispose() { }
    }

    private sealed class CapturingLoggerFactory(ILoggerFactory inner, ConcurrentQueue<string> entries) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(inner.CreateLogger(categoryName), entries);
        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
        public void Dispose() => inner.Dispose();

        private sealed class CaptureLogger(ILogger innerLogger, ConcurrentQueue<string> captured) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => innerLogger.BeginScope(state);
            // 测试捕获独立于下游过滤及共享日志生命周期，源生成日志也必须实际进入收集器。
            public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel != LogLevel.None) captured.Enqueue(formatter(state, exception) + exception?.ToString());
                innerLogger.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }

    public TestContext TestContext { get; set; } = null!;
}
