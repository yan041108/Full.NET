using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class PasswordRecoveryAccountBindingAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var delivery = new DeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services => services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery));
        await scopedFactory.InitializeAsync();
        using var client = scopedFactory.CreateClientForHost("localhost");
        var token = await AccountLifecycleAssertions.LoginAsHostAdminAsync(client, CancellationToken.None);
        var email = $"reassigned-{Guid.NewGuid():N}@example.com";
        var original = await AccountLifecycleAssertions.CreateHostUserWithEmailAsync(client, token, email, CancellationToken.None);
        await RequestAsync();
        var oldIntent = delivery.Intents.Single();

        // 邮箱可由受权档案操作重新分配，但先前签发的恢复凭据不得随邮箱授权给另一账号。
        using (var update = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/identity/users/{original.Id}"))
        {
            update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            update.Content = JsonContent.Create(new
            {
                displayName = original.DisplayName,
                version = original.Version,
                profile = new
                {
                    fieldKeys = new[] { "email" },
                    email = $"moved-{Guid.NewGuid():N}@example.com",
                    version = original.Profile!.Version,
                },
            });
            using var updated = await client.SendAsync(update);
            Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);
        }
        var replacement = await AccountLifecycleAssertions.CreateHostUserWithEmailAsync(client, token, email, CancellationToken.None);
        Assert.AreNotEqual(original.Id, replacement.Id);
        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var beforeOriginal = await ReadUserAsync(original.Id);
        var beforeReplacement = await ReadUserAsync(replacement.Id);
        using (var rejected = await ConfirmAsync(oldIntent))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode,
                "邮箱重新分配后，旧挑战不能修改当前占用该邮箱的另一账号。");
            using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, body.RootElement.GetProperty("code").GetString());
        }
        AssertUnchanged(beforeOriginal, await ReadUserAsync(original.Id));
        AssertUnchanged(beforeReplacement, await ReadUserAsync(replacement.Id));
        var oldRecord = await query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
            AccountChallengeSql.FindById, IdentitySqlParameters.Create(("ChallengeId", oldIntent.ChallengeId)));
        Assert.IsNotNull(oldRecord);
        Assert.IsNull(oldRecord.ConsumedAtUtc);

        await RequestAsync();
        Assert.AreEqual(2, delivery.Intents.Count);
        var fresh = delivery.Intents[1];
        using (var confirmed = await ConfirmAsync(fresh))
        {
            Assert.AreEqual(HttpStatusCode.NoContent, confirmed.StatusCode);
        }
        AssertUnchanged(beforeOriginal, await ReadUserAsync(original.Id));
        var changedReplacement = await ReadUserAsync(replacement.Id);
        Assert.AreNotEqual(beforeReplacement.PasswordHash, changedReplacement.PasswordHash);
        Assert.AreNotEqual(beforeReplacement.SecurityStamp, changedReplacement.SecurityStamp);
        using var replay = await ConfirmAsync(fresh);
        Assert.AreEqual(HttpStatusCode.BadRequest, replay.StatusCode);

        async Task RequestAsync()
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/recover-password",
                new RequestPasswordRecoveryRequest(email));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        Task<HttpResponseMessage> ConfirmAsync(IdentityChallengeDeliveryIntent intent) =>
            client.PostAsJsonAsync("/api/v1/auth/recover-password/confirm",
                new ConfirmPasswordRecoveryRequest(intent.ChallengeId, intent.Credential, "FullNet!2026Recovered"));
        async Task<IdentityUserRecord> ReadUserAsync(Guid id)
        {
            var user = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(new SqlStatement(
                "test.identity_recovery_account_security_state",
                "SELECT PasswordHash, SecurityStamp, Version FROM fn_identity_user WHERE Id = @UserId",
                SqlDataScope.Global), IdentitySqlParameters.Create(("UserId", id)));
            Assert.IsNotNull(user);
            return user;
        }
    }

    private static void AssertUnchanged(IdentityUserRecord before, IdentityUserRecord after)
    {
        Assert.AreEqual(before.PasswordHash, after.PasswordHash);
        Assert.AreEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.AreEqual(before.Version, after.Version);
    }

    private sealed class DeliveryPort : IIdentityChallengeDeliveryPort
    {
        public List<IdentityChallengeDeliveryIntent> Intents { get; } = [];
        public Task<Result<bool>> SendAsync(IdentityChallengeDeliveryIntent intent, CancellationToken cancellationToken = default)
        {
            Intents.Add(intent);
            return Task.FromResult(Result<bool>.Success(true));
        }
    }
}
