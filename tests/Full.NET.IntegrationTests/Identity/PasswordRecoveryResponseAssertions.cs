using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Identity;

internal static class PasswordRecoveryResponseAssertions
{
    public static async Task VerifyAsync(FullNetApiFactory factory)
    {
        var clock = new FixedClock();
        var delivery = new DeliveryPort();
        using var scopedFactory = new FullNetApiFactory(factory.Provider, factory.ConnectionString,
            configureTestServices: services =>
            {
                services.AddSingleton<IClock>(clock);
                services.AddSingleton<IIdentityChallengeDeliveryPort>(delivery);
            });
        await scopedFactory.InitializeAsync();
        using var client = scopedFactory.CreateClientForHost("localhost");
        var adminToken = await AccountLifecycleAssertions.LoginAsHostAdminAsync(client, CancellationToken.None);
        var suffix = Guid.NewGuid().ToString("N");
        var activeEmail = $"active-{suffix}@example.com";
        var inactiveEmail = $"inactive-{suffix}@example.com";
        var unknownEmail = $"unknown-{suffix}@example.com";
        await AccountLifecycleAssertions.CreateHostUserWithEmailAsync(client, adminToken, activeEmail, CancellationToken.None);
        var inactive = await AccountLifecycleAssertions.CreateHostUserWithEmailAsync(client, adminToken, inactiveEmail, CancellationToken.None);
        using (var disable = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/identity/users/{inactive.Id}/disable"))
        {
            disable.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            using var disabled = await client.SendAsync(disable);
            Assert.AreEqual(HttpStatusCode.OK, disabled.StatusCode);
        }

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var responses = new List<AccountChallengeAcceptedResponse>();
        responses.Add(await RequestAsync(activeEmail));
        Assert.AreEqual(1, delivery.Intents.Count);
        var activeIntent = delivery.Intents[0];
        Assert.AreEqual(activeIntent.ChallengeId, responses[0].ChallengeId);
        var activeRecord = await ReadAsync(activeIntent.ChallengeId);
        Assert.IsNotNull(activeRecord);
        Assert.IsNull(activeRecord.ConsumedAtUtc);

        responses.Add(await RequestAsync(unknownEmail));
        responses.Add(await RequestAsync(inactiveEmail));
        responses.Add(await RequestAsync("invalid"));
        Assert.AreEqual(1, delivery.Intents.Count, "未知或停用账号不得发送恢复邮件。");
        Assert.AreEqual(0, await CountAsync(unknownEmail));
        Assert.AreEqual(0, await CountAsync(inactiveEmail));
        Assert.AreEqual(0, await CountAsync("invalid"));
        delivery.Reject = true;
        responses.Add(await RequestAsync(activeEmail));
        Assert.AreEqual(2, delivery.Intents.Count);
        var rejectedIntent = delivery.Intents[1];
        var rejectedRecord = await ReadAsync(rejectedIntent.ChallengeId);
        Assert.IsNotNull(rejectedRecord);
        Assert.IsNotNull(rejectedRecord.ConsumedAtUtc, "投递失败仍须撤销该次真实挑战。");

        foreach (var outcome in new[] { "exception", "timeout", "not-accepted" })
        {
            delivery.Outcome = outcome;
            responses.Add(await RequestAsync(activeEmail));
            var failedIntent = delivery.Intents[^1];
            var failedRecord = await ReadAsync(failedIntent.ChallengeId);
            Assert.IsNotNull(failedRecord);
            Assert.IsNotNull(failedRecord.ConsumedAtUtc, "异常或未受理的投递必须补偿本次真实挑战。");
            Assert.AreEqual(2, failedRecord.Version);
            Assert.AreNotEqual(failedIntent.ChallengeId, responses[^1].ChallengeId);
            using var rejectedConfirm = await client.PostAsJsonAsync("/api/v1/auth/recover-password/confirm",
                new ConfirmPasswordRecoveryRequest(failedIntent.ChallengeId, failedIntent.Credential, "FullNet!2026Recovered"));
            Assert.AreEqual(HttpStatusCode.BadRequest, rejectedConfirm.StatusCode);
            using var rejectedProblem = JsonDocument.Parse(await rejectedConfirm.Content.ReadAsStringAsync());
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, rejectedProblem.RootElement.GetProperty("code").GetString());
        }
        Assert.AreEqual(5, delivery.Intents.Count);

        // 固定时钟只用于比较窗口；此测试不把响应正文一致当成真实耗时或 SMTP 无枚举证明。
        foreach (var response in responses)
        {
            Assert.AreNotEqual(Guid.Empty, response.ChallengeId, "不得由空标识区分账号或投递状态。");
            Assert.AreEqual(7, response.ChallengeId.Version);
            Assert.AreEqual(clock.UtcNow.AddMinutes(15), response.ExpiresAtUtc);
        }
        Assert.AreEqual(responses.Count, responses.Select(item => item.ChallengeId).Distinct().Count());
        foreach (var placeholder in responses.Skip(1))
        {
            Assert.IsNull(await ReadAsync(placeholder.ChallengeId), "占位标识不对应任何可消费挑战。");
            using var confirm = await client.PostAsJsonAsync("/api/v1/auth/recover-password/confirm",
                new ConfirmPasswordRecoveryRequest(placeholder.ChallengeId, activeIntent.Credential, "FullNet!2026Recovered"));
            Assert.AreEqual(HttpStatusCode.BadRequest, confirm.StatusCode);
            using var problem = JsonDocument.Parse(await confirm.Content.ReadAsStringAsync());
            Assert.AreEqual(IdentityErrorCodes.AccountChallengeInvalid, problem.RootElement.GetProperty("code").GetString());
        }

        async Task<AccountChallengeAcceptedResponse> RequestAsync(string email)
        {
            using var response = await client.PostAsJsonAsync("/api/v1/auth/recover-password", new RequestPasswordRecoveryRequest(email));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            CollectionAssert.AreEquivalent(new[] { "challengeId", "expiresAtUtc" },
                body.RootElement.EnumerateObject().Select(item => item.Name).ToArray());
            return new AccountChallengeAcceptedResponse(body.RootElement.GetProperty("challengeId").GetGuid(),
                body.RootElement.GetProperty("expiresAtUtc").GetDateTimeOffset());
        }
        Task<AccountChallengeRecord?> ReadAsync(Guid id) => query.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
            AccountChallengeSql.FindById, IdentitySqlParameters.Create(("ChallengeId", id)));
        Task<int> CountAsync(string email) => query.QuerySingleOrDefaultAsync<int>(new SqlStatement(
            "test.identity_recovery_rows_by_email",
            "SELECT COUNT(*) FROM fn_identity_account_challenge WHERE NormalizedEmail = @NormalizedEmail",
            SqlDataScope.Global), IdentitySqlParameters.Create(("NormalizedEmail", email)));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }

    private sealed class DeliveryPort : IIdentityChallengeDeliveryPort
    {
        public bool Reject { get; set; }
        public string? Outcome { get; set; }
        public List<IdentityChallengeDeliveryIntent> Intents { get; } = [];
        public Task<Result<bool>> SendAsync(IdentityChallengeDeliveryIntent intent, CancellationToken cancellationToken = default)
        {
            Intents.Add(intent);
            if (Outcome == "exception") throw new IOException("sensitive-delivery-detail");
            if (Outcome == "timeout") throw new OperationCanceledException("sensitive-delivery-detail");
            if (Outcome == "not-accepted") return Task.FromResult(Result<bool>.Success(false));
            return Task.FromResult(Reject
                ? Result<bool>.Failure(new Error(IdentityErrorCodes.AccountChallengeDeliveryFailed, "Test delivery rejected.", ErrorType.BusinessRule))
                : Result<bool>.Success(true));
        }
    }
}
