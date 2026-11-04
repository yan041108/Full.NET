using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class PasswordRecoveryLifecycleAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var delivery = new CapturingIdentityChallengeDeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services => services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery));
        await scopedFactory.InitializeAsync();
        using var client = scopedFactory.CreateClientForHost("localhost");
        var adminToken = await AccountLifecycleAssertions.LoginAsHostAdminAsync(client, CancellationToken.None);
        // 每种真实账号操作都验证旧码失效与新码可用，不用直接改表代替业务接口。
        foreach (var scenario in new[] { "admin-reset", "self-change", "disable-enable" })
        {
            var email = $"lifecycle-{Guid.NewGuid():N}@example.com";
            var user = await AccountLifecycleAssertions.CreateHostUserWithEmailAsync(client, adminToken, email, CancellationToken.None);
            await RequestAsync(email);
            var old = delivery.LastIntent!;
            var before = await ReadUserAsync(user.Id);
            if (scenario == "disable-enable")
            {
                await ManageAsync($"/api/v1/identity/users/{user.Id}/disable", null, adminToken);
                await ManageAsync($"/api/v1/identity/users/{user.Id}/enable", null, adminToken);
            }
            else if (scenario == "self-change")
            {
                // 原始登录不偷偷完成首次改密，目标变更只能来自本次显式请求。
                using var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
                { Content = JsonContent.Create(new LoginRequest(user.Username, FullNetApiFactory.TestPassword)) };
                login.Headers.Add("Origin", "http://localhost");
                using var loggedIn = await client.SendAsync(login);
                Assert.AreEqual(HttpStatusCode.OK, loggedIn.StatusCode);
                var token = await loggedIn.Content.ReadFromJsonAsync<TokenResponse>();Assert.IsNotNull(token);
                Assert.IsTrue(loggedIn.Headers.TryGetValues("Set-Cookie", out var values));
                var cookies = values!.Select(value => value.Split(';', 2)[0]).ToArray();
                var csrf = cookies.Single(value => value.StartsWith("fullnet-csrf=", StringComparison.Ordinal))["fullnet-csrf=".Length..];
                using var change = new HttpRequestMessage(HttpMethod.Post, "/api/v1/me/password")
                { Content = JsonContent.Create(new ChangePasswordRequest(FullNetApiFactory.TestPassword, "FullNet!2026Changed")) };
                change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
                change.Headers.Add("Origin", "http://localhost");change.Headers.Add("Cookie", string.Join("; ", cookies));
                change.Headers.Add("X-CSRF-Token", csrf);
                using var changed = await client.SendAsync(change);
                Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
            }
            else
            {
                await ManageAsync($"/api/v1/identity/users/{user.Id}/reset-password",
                    new ResetHostUserPasswordRequest("FullNet!2026Changed"), adminToken);
            }
            var authoritative = await ReadUserAsync(user.Id);
            Assert.IsTrue(authoritative.IsActive);
            Assert.AreNotEqual(before.SecurityStamp, authoritative.SecurityStamp, scenario);
            using (var rejected = await ConfirmAsync(old))
            {
                Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode, scenario);
                using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, problem.RootElement.GetProperty("code").GetString());
            }
            var after = await ReadUserAsync(user.Id);
            Assert.AreEqual(authoritative.PasswordHash, after.PasswordHash);
            Assert.AreEqual(authoritative.SecurityStamp, after.SecurityStamp);
            Assert.AreEqual(authoritative.Version, after.Version);
            await using (var scope = scopedFactory.Services.CreateAsyncScope())
            {
                var record = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                    .QuerySingleOrDefaultAsync<AccountChallengeRecord>(AccountChallengeSql.FindById,
                        IdentitySqlParameters.Create(("ChallengeId", old.ChallengeId)));
                Assert.IsNotNull(record);Assert.IsNull(record.ConsumedAtUtc);
                Assert.AreEqual(1, record.AttemptCount);
            }
            await RequestAsync(email);
            var fresh = delivery.LastIntent!;
            Assert.AreNotEqual(old.ChallengeId, fresh.ChallengeId);
            using var accepted = await ConfirmAsync(fresh);
            Assert.AreEqual(HttpStatusCode.NoContent, accepted.StatusCode, scenario);
            using var replay = await ConfirmAsync(fresh);
            Assert.AreEqual(HttpStatusCode.BadRequest, replay.StatusCode, scenario);
        }

        async Task RequestAsync(string email)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/recover-password", new RequestPasswordRecoveryRequest(email));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);Assert.IsNotNull(delivery.LastIntent);
        }
        Task<HttpResponseMessage> ConfirmAsync(IdentityChallengeDeliveryIntent intent) => client.PostAsJsonAsync(
            "/api/v1/auth/recover-password/confirm", new ConfirmPasswordRecoveryRequest(intent.ChallengeId,
                intent.Credential, "FullNet!2026Recovered"));
        async Task ManageAsync(string path, object? request, string token)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (request is not null) message.Content = JsonContent.Create(request);
            using var response = await client.SendAsync(message);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path);
        }
        async Task<IdentityUserRecord> ReadUserAsync(Guid id)
        {
            await using var scope = scopedFactory.Services.CreateAsyncScope();
            var user = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<IdentityUserRecord>(
                new SqlStatement("test.identity_recovery_lifecycle_state",
                    "SELECT PasswordHash, SecurityStamp, Version, IsActive FROM fn_identity_user WHERE Id = @UserId", SqlDataScope.Global),
                IdentitySqlParameters.Create(("UserId", id)));
            Assert.IsNotNull(user);return user;
        }
    }
}
