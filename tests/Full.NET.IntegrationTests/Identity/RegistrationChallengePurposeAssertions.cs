using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.AccountChallenges;
using Full.NET.Modules.Identity.Features.ManageRegistrationPolicy;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class RegistrationChallengePurposeAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var delivery = new CapturingDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services => services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery));
        await scopedFactory.InitializeAsync();
        using var client = scopedFactory.CreateClientForHost("localhost");
        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var challenges = scope.ServiceProvider.GetRequiredService<AccountChallengeService>();
        var policy = scope.ServiceProvider.GetRequiredService<RegistrationPolicyService>();
        var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var email = $"purpose-{Guid.NewGuid():N}@example.com";
        var existing = new List<AccountChallengeRecord>();
        context.SetHost();
        try
        {
            // 受信服务建立三种合法挑战；非法注册请求不得创建新行或撤销任何已有挑战。
            foreach (var purpose in new[]
            {
                IdentityAccountChallengePurpose.RegistrationEmailVerification,
                IdentityAccountChallengePurpose.PasswordRecovery,
                IdentityAccountChallengePurpose.InvitationEmailVerification,
            })
            {
                var created = await challenges.CreateAndDeliverAsync(purpose, email);
                Assert.IsTrue(created.IsSuccess);
                existing.Add((await ReadAsync(created.Value!.ChallengeId))!);
            }
            var baselineDeliveries = delivery.Intents.Count;
            var baselineRows = await CountAsync();
            foreach (var mode in new[] { IdentityRegistrationMode.InvitationOnly, IdentityRegistrationMode.Open })
            {
                var current = await policy.GetAsync();
                Assert.IsTrue(current.IsSuccess);
                Assert.IsTrue((await policy.UpdateAsync(new UpdateRegistrationPolicyRequest(
                    mode == IdentityRegistrationMode.Open, current.Value!.Version, mode))).IsSuccess);
                foreach (var purpose in new[] { (byte)IdentityAccountChallengePurpose.PasswordRecovery, (byte)0, (byte)4, byte.MaxValue })
                foreach (var includeInvitation in new[] { false, true })
                {
                    using var response = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
                        new SendRegistrationEmailChallengeRequest(email, (IdentityAccountChallengePurpose)purpose,
                            includeInvitation ? Guid.CreateVersion7() : null, includeInvitation ? "invalid-invitation" : null));
                    Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"mode={mode}, purpose={purpose}");
                    using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.AreEqual(ValidationErrorCodes.Failed, problem.RootElement.GetProperty("code").GetString());
                    Assert.AreEqual(baselineDeliveries, delivery.Intents.Count, "非法用途不得触发邮件投递。");
                    Assert.AreEqual(baselineRows, await CountAsync(), "非法用途不得创建任何挑战记录。");
                    foreach (var record in existing) Assert.AreEqual(record, await ReadAsync(record.ChallengeId));
                }

                // 合法用途仍受原注册政策和邀请凭据约束；开放注册继续允许正常邮箱挑战。
                using var registration = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
                    new SendRegistrationEmailChallengeRequest(email, IdentityAccountChallengePurpose.RegistrationEmailVerification));
                Assert.AreEqual(mode == IdentityRegistrationMode.Open ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
                    registration.StatusCode);
                using var invitation = await client.PostAsJsonAsync("/api/v1/auth/register/email-challenge",
                    new SendRegistrationEmailChallengeRequest(email, IdentityAccountChallengePurpose.InvitationEmailVerification));
                Assert.AreEqual(HttpStatusCode.BadRequest, invitation.StatusCode);
            }
            Assert.AreEqual(baselineDeliveries + 1, delivery.Intents.Count);
        }
        finally { context.Clear(); }

        Task<AccountChallengeRecord?> ReadAsync(Guid id) => query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
            AccountChallengeSql.FindById, IdentitySqlParameters.Create(("ChallengeId", id)));
        Task<int> CountAsync() => query.QuerySingleOrDefaultAsync<int>(new SqlStatement(
            "test.identity_challenge_rows_by_email",
            "SELECT COUNT(*) FROM fn_identity_account_challenge WHERE NormalizedEmail = @NormalizedEmail",
            SqlDataScope.Global), IdentitySqlParameters.Create(("NormalizedEmail", email)));
    }

    private sealed class CapturingDeliveryPort : IIdentityChallengeDeliveryPort
    {
        public List<IdentityChallengeDeliveryIntent> Intents { get; } = [];

        public Task<Result<bool>> SendAsync(IdentityChallengeDeliveryIntent intent, CancellationToken cancellationToken = default)
        {
            Intents.Add(intent);
            return Task.FromResult(Result<bool>.Success(true));
        }
    }
}
