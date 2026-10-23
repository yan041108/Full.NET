using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static Full.NET.IntegrationTests.Notifications.NotificationDeliveryWorkerAssertions;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>复用真实双库宿主验证发送意愿复检、多维配额与持久化台账，而不以 Adapter 调用代替送达证明。</summary>
internal static class NotificationSendGovernanceAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, CancellationToken cancellationToken = default)
    {
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, cancellationToken);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, cancellationToken);
        await NotificationGovernanceAuthorizationAssertions.VerifyAsync(factory, client, cancellationToken);
        // 个人渠道目录不能要求配置控制面权限；只读令牌仍不得更新偏好。
        var preferenceReadToken = await factory.CreateHostAccessTokenAsync(
            [NotificationPlatformPermissions.PreferencesRead], cancellationToken);
        using (var channelsResponse = await SendAsync(client, preferenceReadToken, HttpMethod.Get,
            "/api/v1/notifications/my-preferences/channels", null, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, channelsResponse.StatusCode);
            var channels = await channelsResponse.Content.ReadFromJsonAsync<string[]>(cancellationToken);
            Assert.IsNotNull(channels);
            CollectionAssert.Contains(channels, "inbox");
            CollectionAssert.Contains(channels, "test");
            Assert.AreEqual(channels.Length, channels.Distinct(StringComparer.Ordinal).Count());
        }
        using (var forbidden = await SendAsync(client, preferenceReadToken, HttpMethod.Put,
            "/api/v1/notifications/my-preferences/test",
            new UpdateMyNotificationPreferenceRequest("test", false, false, null), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        var harness = factory.Services.GetRequiredService<TestNotificationProviderHarness>();
        harness.Reset();
        harness.AllowsAutomaticRetryAfterUnknownResult = false;
        await ProcessPendingAsync(factory, cancellationToken);

        var marketing = await CreateRouteAsync(client, token, user.Id, cancellationToken, "marketing");
        var transactional = await CreateRouteAsync(client, token, user.Id, cancellationToken);
        await SetPreferenceAsync(client, token, false, null, cancellationToken);
        harness.Reset();
        var suppressed = await CreateIntentAsync(client, token, marketing, user.Id, "no-consent", cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var suppressedDelivery = await WaitDeliveryAsync(client, token, suppressed.Id, cancellationToken);
        Assert.AreEqual("suppressed", suppressedDelivery.StatusKey);
        Assert.AreEqual(0, harness.IdempotencyKeysFor(suppressedDelivery.Id).Count);
        var suppressedRow = await ReadDeliveryAsync(factory, suppressedDelivery.Id, cancellationToken);
        Assert.AreEqual(NotificationsErrorCodes.PolicyMarketingConsentRequired, suppressedRow.SuppressionReasonCode);

        await SetPreferenceAsync(client, token, true, null, cancellationToken);
        var queued = await CreateIntentAsync(client, token, marketing, user.Id, "revoke-consent", cancellationToken);
        var queuedDelivery = await WaitDeliveryAsync(client, token, queued.Id, cancellationToken);
        Assert.AreEqual("accepted", queuedDelivery.StatusKey);
        await SetPreferenceAsync(client, token, false, null, cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        queuedDelivery = await GetDeliveryAsync(client, token, queuedDelivery.Id, cancellationToken);
        Assert.AreEqual("suppressed", queuedDelivery.StatusKey);
        Assert.AreEqual(0, harness.IdempotencyKeysFor(queuedDelivery.Id).Count);
        Assert.AreEqual(NotificationsErrorCodes.PolicyMarketingConsentRequired,
            (await ReadDeliveryAsync(factory, queuedDelivery.Id, cancellationToken)).SuppressionReasonCode);

        // 先入队，再进入静默，确保命中 Worker 复检而不是仅验证 Intent 受理时的静默判定。
        var quietIntent = await CreateIntentAsync(client, token, transactional, user.Id, "quiet-after-queue", cancellationToken);
        var quietDelivery = await WaitDeliveryAsync(client, token, quietIntent.Id, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var quietJson = JsonSerializer.Serialize(new
        {
            timeZone = "UTC",
            start = now.AddHours(-1).ToString("HH:mm"),
            end = now.AddHours(1).ToString("HH:mm"),
        });
        await SetPreferenceAsync(client, token, false, quietJson, cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        quietDelivery = await GetDeliveryAsync(client, token, quietDelivery.Id, cancellationToken);
        Assert.AreEqual("accepted", quietDelivery.StatusKey);
        Assert.AreEqual(0, quietDelivery.Attempts.Count);
        Assert.AreEqual(0, harness.IdempotencyKeysFor(quietDelivery.Id).Count);
        Assert.IsNotNull(quietDelivery.NextAttemptAtUtc);
        var expectedEnd = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero)
            .AddHours(1);
        Assert.AreEqual(expectedEnd, quietDelivery.NextAttemptAtUtc.Value);
        await ProcessPendingAsync(factory, cancellationToken);
        Assert.AreEqual(0, harness.IdempotencyKeysFor(quietDelivery.Id).Count);

        using (var invalid = await SendAsync(client, token, HttpMethod.Put, "/api/v1/notifications/my-preferences/test",
            new UpdateMyNotificationPreferenceRequest("test", false, false,
                """{"timeZone":"No/Such_Zone","start":"22:00","end":"08:00"}"""), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await SetPreferenceAsync(client, token, false, null, cancellationToken);

        // 精确 Provider 政策优先于基线通配；两条真实 Worker 连接竞争一个窗口额度。
        var providerPolicy = await CreateQuotaAsync(client, token, NotificationQuotaDimensions.Provider,
            TestNotificationProvider.ProviderTypeKeyValue, cancellationToken);
        try
        {
            harness.Reset();
            var replayKey = $"governance-{Guid.NewGuid():N}";
            var first = await CreateReplayIntentAsync(client, token, transactional, user.Id, replayKey, cancellationToken);
            var replay = await CreateReplayIntentAsync(client, token, transactional, user.Id, replayKey, cancellationToken);
            Assert.AreEqual(first.Id, replay.Id);
            Assert.AreEqual(1, await CountDeliveriesAsync(factory, first.Id, cancellationToken));
            var second = await CreateIntentAsync(client, token, transactional, user.Id, "quota-competitor", cancellationToken);
            var firstDelivery = await WaitDeliveryAsync(client, token, first.Id, cancellationToken);
            var secondDelivery = await WaitDeliveryAsync(client, token, second.Id, cancellationToken);
            var frozen = (await ReadDeliveryAsync(factory, firstDelivery.Id, cancellationToken)).ContentSnapshotJson;
            using (var snapshot = JsonDocument.Parse(frozen!))
            {
                Assert.IsFalse(string.IsNullOrEmpty(snapshot.RootElement.GetProperty("localeTag").GetString()));
                Assert.AreNotEqual(Guid.Empty, snapshot.RootElement.GetProperty("templateVersionId").GetGuid());
                Assert.IsFalse(string.IsNullOrEmpty(snapshot.RootElement.GetProperty("contentHash").GetString()));
            }
            await Task.WhenAll(ProcessPendingAsync(factory, cancellationToken), ProcessPendingAsync(factory, cancellationToken));
            firstDelivery = await GetDeliveryAsync(client, token, firstDelivery.Id, cancellationToken);
            secondDelivery = await GetDeliveryAsync(client, token, secondDelivery.Id, cancellationToken);
            var results = new[] { firstDelivery, secondDelivery };
            Assert.AreEqual(1, results.Count(item => item.StatusKey == "sent"));
            var blocked = results.Single(item => item.StatusKey == "accepted");
            Assert.IsNotNull(blocked.NextAttemptAtUtc);
            Assert.AreEqual(0, blocked.Attempts.Count);
            Assert.AreEqual(1, results.Sum(item => harness.IdempotencyKeysFor(item.Id).Count));
            Assert.AreEqual(frozen, (await ReadDeliveryAsync(factory, firstDelivery.Id, cancellationToken)).ContentSnapshotJson);
            await AssertReservationStatesAsync(factory, results.Single(item => item.StatusKey == "sent").Id,
                NotificationQuotaReservationStates.Consumed, cancellationToken);
            Assert.AreEqual(0, (await ReservationStatesAsync(factory, blocked.Id, cancellationToken)).Count);
        }
        finally
        {
            await SetQuotaEnabledAsync(client, token, providerPolicy, false, cancellationToken);
        }

        // 每个后续场景声明独立 Producer 精确额度，避免成功核销和 Unknown 暂挂相互污染。
        var unknownRoute = await CreateRouteAsync(client, token, user.Id, cancellationToken);
        await CreateQuotaAsync(client, token, NotificationQuotaDimensions.Producer, unknownRoute.Producer, cancellationToken);
        harness.Reset(TestNotificationProviderMode.Unknown);
        var unknown = await CreateIntentAsync(client, token, unknownRoute, user.Id, "unknown-quota", cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var unknownDelivery = await WaitDeliveryAsync(client, token, unknown.Id, cancellationToken);
        Assert.AreEqual("unknown", unknownDelivery.StatusKey);
        Assert.IsNull(unknownDelivery.NextAttemptAtUtc);
        await AssertReservationStatesAsync(factory, unknownDelivery.Id,
            NotificationQuotaReservationStates.PendingReconciliation, cancellationToken);
        var afterUnknown = await CreateIntentAsync(client, token, unknownRoute, user.Id, "unknown-blocks", cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var afterUnknownDelivery = await WaitDeliveryAsync(client, token, afterUnknown.Id, cancellationToken);
        Assert.AreEqual("accepted", afterUnknownDelivery.StatusKey);
        Assert.IsNotNull(afterUnknownDelivery.NextAttemptAtUtc);
        Assert.AreEqual(0, harness.IdempotencyKeysFor(afterUnknownDelivery.Id).Count);

        var releaseRoute = await CreateRouteAsync(client, token, user.Id, cancellationToken);
        await CreateQuotaAsync(client, token, NotificationQuotaDimensions.Producer, releaseRoute.Producer, cancellationToken);
        harness.Reset(TestNotificationProviderMode.Permanent);
        var permanent = await CreateIntentAsync(client, token, releaseRoute, user.Id, "release-quota", cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var permanentDelivery = await WaitDeliveryAsync(client, token, permanent.Id, cancellationToken);
        Assert.AreEqual("failed", permanentDelivery.StatusKey);
        await AssertReservationStatesAsync(factory, permanentDelivery.Id,
            NotificationQuotaReservationStates.Released, cancellationToken);
        harness.Reset();
        var success = await CreateIntentAsync(client, token, releaseRoute, user.Id, "released-can-send", cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var successDelivery = await WaitDeliveryAsync(client, token, success.Id, cancellationToken);
        Assert.AreEqual("sent", successDelivery.StatusKey);
        Assert.AreEqual(1, harness.IdempotencyKeysFor(successDelivery.Id).Count);
        await AssertReservationStatesAsync(factory, successDelivery.Id,
            NotificationQuotaReservationStates.Consumed, cancellationToken);

        var readToken = await factory.CreateHostAccessTokenAsync([NotificationPlatformPermissions.QuotasRead], cancellationToken);
        using (var forbidden = await SendAsync(client, readToken, HttpMethod.Post, "/api/v1/notifications/quotas",
            new CreateNotificationQuotaPolicyRequest("provider", "unauthorized", 60, 1), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using (var outstanding = await SendAsync(client, readToken, HttpMethod.Get,
            "/api/v1/notifications/quotas/outstanding", null, cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, outstanding.StatusCode);
            var rows = await outstanding.Content.ReadFromJsonAsync<NotificationQuotaOutstandingResponse[]>(cancellationToken);
            Assert.IsNotNull(rows);
            Assert.IsTrue(rows.Any(row => row.StateKey == NotificationQuotaReservationStates.PendingReconciliation));
        }

        // 可信回执只核销原尝试的未决预留，回执重放不得重复调整窗口计数。
        var receiptBody = JsonSerializer.SerializeToUtf8Bytes(new
        {
            receiptIdempotencyKey = $"governance-{unknownDelivery.Id:N}",
            providerMessageId = unknownDelivery.Attempts.Single().ProviderMessageId,
            externalStatusKey = "delivered",
            mappedStatusKey = "delivered",
        });
        for (var repeat = 0; repeat < 2; repeat++)
        {
            using var receipt = await SendReceiptAsync(client, TestNotificationProvider.ProviderTypeKeyValue,
                receiptBody, TestNotificationReceiptVerifier.Sign(receiptBody), cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, receipt.StatusCode);
            await AssertReservationStatesAsync(factory, unknownDelivery.Id,
                NotificationQuotaReservationStates.Consumed, cancellationToken);
        }
        Assert.AreEqual("delivered",
            (await GetDeliveryAsync(client, token, unknownDelivery.Id, cancellationToken)).StatusKey);

        // 模拟预留提交后进程中断：新世代不能把未决额度当作确定失败释放，且不得再次外发。
        var abandonedRoute = await CreateRouteAsync(client, token, user.Id, cancellationToken);
        await CreateQuotaAsync(client, token, NotificationQuotaDimensions.Producer, abandonedRoute.Producer, cancellationToken);
        harness.Reset();
        var abandonedIntent = await CreateIntentAsync(client, token, abandonedRoute, user.Id, "abandoned-quota", cancellationToken);
        var abandonedDelivery = await WaitDeliveryAsync(client, token, abandonedIntent.Id, cancellationToken);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
                await command.ExecuteAsync(new SqlStatement("tests.notifications.claim_for_abandoned_quota",
                        """
                        UPDATE fn_notifications_delivery
                        SET LeaseGeneration = LeaseGeneration + 1, Revision = Revision + 1,
                            AttemptBudgetUsed = AttemptBudgetUsed + 1,
                            LeaseOwnerKey = 'interrupted-worker', LeaseExpiresAtUtc = @LeaseExpiresAtUtc
                        WHERE Id = @Id AND LeaseOwnerKey IS NULL
                        """, SqlDataScope.HostOnly),
                    NotificationPlatformSqlParameters.Create(("Id", abandonedDelivery.Id),
                        ("LeaseExpiresAtUtc", DateTimeOffset.UtcNow.AddMinutes(1))), cancellationToken);
                var row = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                    .QuerySingleOrDefaultAsync<NotificationDeliveryRecord>(NotificationPlatformSql.FindDeliveryById,
                        NotificationPlatformSqlParameters.Create(("Id", abandonedDelivery.Id)), cancellationToken);
                Assert.IsNotNull(row);
                var reserved = await scope.ServiceProvider.GetRequiredService<NotificationQuotaGovernor>()
                    .ReserveAsync(row.Id, row.LeaseGeneration, "host", TestNotificationProvider.ProviderTypeKeyValue,
                        abandonedRoute.Producer, abandonedRoute.Scene, cancellationToken);
                Assert.IsTrue(reserved.IsReserved);
                await command.ExecuteAsync(NotificationPlatformSql.ExpireDeliveryLease,
                    NotificationPlatformSqlParameters.Create(("Id", row.Id),
                        ("ExpiredAt", DateTimeOffset.UtcNow.AddSeconds(-1))), cancellationToken);
            }
            finally
            {
                tenant.Clear();
            }
        }
        await ProcessPendingAsync(factory, cancellationToken);
        abandonedDelivery = await GetDeliveryAsync(client, token, abandonedDelivery.Id, cancellationToken);
        Assert.AreEqual("unknown", abandonedDelivery.StatusKey);
        Assert.IsNull(abandonedDelivery.NextAttemptAtUtc);
        Assert.AreEqual(2, abandonedDelivery.Attempts.Single().AttemptNumber,
            "中断领取消耗预算但没有写入首条 Attempt，历史序号允许存在空档。");
        Assert.AreEqual(0, harness.IdempotencyKeysFor(abandonedDelivery.Id).Count);
        await AssertReservationStatesAsync(factory, abandonedDelivery.Id,
            NotificationQuotaReservationStates.PendingReconciliation, cancellationToken);

        // 待对账已落库后再次人工重投，仍不能绕过旧世代未决调用或重新预留额度。
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            tenant.SetHost();
            try
            {
                var row = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                    .QuerySingleOrDefaultAsync<NotificationDeliveryRecord>(NotificationPlatformSql.FindDeliveryById,
                        NotificationPlatformSqlParameters.Create(("Id", abandonedDelivery.Id)), cancellationToken);
                Assert.IsNotNull(row);
                Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<ICommandExecutor>()
                    .ExecuteAsync(NotificationPlatformSql.RetryDelivery,
                        NotificationPlatformSqlParameters.Create(("Id", row.Id), ("Revision", row.Revision),
                            ("NextAttemptAtUtc", DateTimeOffset.UtcNow), ("Now", DateTimeOffset.UtcNow)), cancellationToken));
            }
            finally
            {
                tenant.Clear();
            }
        }
        await ProcessPendingAsync(factory, cancellationToken);
        abandonedDelivery = await GetDeliveryAsync(client, token, abandonedDelivery.Id, cancellationToken);
        Assert.AreEqual("unknown", abandonedDelivery.StatusKey);
        Assert.IsNull(abandonedDelivery.NextAttemptAtUtc);
        CollectionAssert.AreEqual(new[] { 2 }, abandonedDelivery.Attempts
            .Select(attempt => attempt.AttemptNumber).Order().ToArray(),
            "未对账 Unknown 的重投被拒，不能重新领取或增长尝试序号。");
        Assert.AreEqual(0, harness.IdempotencyKeysFor(abandonedDelivery.Id).Count);
        await AssertReservationStatesAsync(factory, abandonedDelivery.Id,
            NotificationQuotaReservationStates.PendingReconciliation, cancellationToken);

        // 精确 Provider 已停用，最后暂时关闭其通配基线，验证缺失维度确实失败关闭；完成后恢复。
        using var list = await SendAsync(client, token, HttpMethod.Get,
            "/api/v1/notifications/quotas?page=1&pageSize=100", null, cancellationToken);
        var policies = await list.Content.ReadFromJsonAsync<PagedResult<NotificationQuotaPolicyResponse>>(cancellationToken);
        Assert.IsNotNull(policies);
        var baseline = policies.Items.Single(row => row.DimensionKey == "provider" && row.DimensionValue == "*");
        var disabled = await SetQuotaEnabledAsync(client, token, baseline, false, cancellationToken);
        try
        {
            harness.Reset();
            var missing = await CreateIntentAsync(client, token, transactional, user.Id, "missing-quota", cancellationToken);
            await ProcessPendingAsync(factory, cancellationToken);
            var missingDelivery = await WaitDeliveryAsync(client, token, missing.Id, cancellationToken);
            Assert.AreEqual("failed", missingDelivery.StatusKey);
            Assert.AreEqual(0, missingDelivery.Attempts.Count);
            Assert.AreEqual(0, harness.IdempotencyKeysFor(missingDelivery.Id).Count);
        }
        finally
        {
            await SetQuotaEnabledAsync(client, token, disabled, true, cancellationToken);
            await SetPreferenceAsync(client, token, false, null, cancellationToken);
            harness.Reset();
        }
    }

    private static async Task SetPreferenceAsync(HttpClient client, string token, bool consent, string? quietJson,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, token, HttpMethod.Put, "/api/v1/notifications/my-preferences/test",
            new UpdateMyNotificationPreferenceRequest("test", false, consent, quietJson), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task<NotificationQuotaPolicyResponse> CreateQuotaAsync(HttpClient client, string token,
        string dimension, string value, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, "/api/v1/notifications/quotas",
            new CreateNotificationQuotaPolicyRequest(dimension, value, 86400, 1), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<NotificationQuotaPolicyResponse>(cancellationToken))!;
    }

    private static async Task<NotificationQuotaPolicyResponse> SetQuotaEnabledAsync(HttpClient client, string token,
        NotificationQuotaPolicyResponse policy, bool enabled, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post,
            $"/api/v1/notifications/quotas/{policy.Id:D}/enabled",
            new SetNotificationQuotaPolicyEnabledRequest(enabled, policy.Version), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<NotificationQuotaPolicyResponse>(cancellationToken))!;
    }

    private static async Task<NotificationIntentResponse> CreateReplayIntentAsync(HttpClient client, string token,
        RouteFixture fixture, Guid userId, string key, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, "/api/v1/notifications/intents",
            new CreateNotificationIntentRequest(fixture.Producer, fixture.Scene, fixture.TemplateKey,
                [new NotificationRecipientInput("user", userId.ToString("N"))],
                JsonSerializer.SerializeToElement(new { orderNo = "same-request" }), key), cancellationToken);
        Assert.IsTrue(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken))!;
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method,
        string path, object? body, CancellationToken cancellationToken)
    {
        var request = NotificationProfileBindingAssertions.CreateBearerJsonRequest(method, path, token, body);
        return SendAndDisposeAsync();
        async Task<HttpResponseMessage> SendAndDisposeAsync()
        {
            using (request) return await client.SendAsync(request, cancellationToken);
        }
    }

    private static async Task<NotificationDeliveryRecord> ReadDeliveryAsync(FullNetApiFactory factory, Guid id,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        return (await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<NotificationDeliveryRecord>(NotificationPlatformSql.FindDeliveryById,
                NotificationPlatformSqlParameters.Create(("Id", id)), cancellationToken))!;
    }

    private static async Task<long> CountDeliveriesAsync(FullNetApiFactory factory, Guid intentId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<long>(
            new SqlStatement("tests.notifications.count_intent_deliveries",
                "SELECT COUNT(*) FROM fn_notifications_delivery WHERE IntentId = @IntentId", SqlDataScope.HostOnly),
            NotificationPlatformSqlParameters.Create(("IntentId", intentId)), cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReservationStatesAsync(FullNetApiFactory factory, Guid deliveryId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QueryAsync<string>(
            new SqlStatement("tests.notifications.reservation_states",
                "SELECT StateKey FROM fn_notifications_quota_reservation WHERE DeliveryId = @DeliveryId", SqlDataScope.HostOnly),
            NotificationPlatformSqlParameters.Create(("DeliveryId", deliveryId)), cancellationToken);
    }

    private static async Task AssertReservationStatesAsync(FullNetApiFactory factory, Guid deliveryId, string expected,
        CancellationToken cancellationToken)
    {
        var states = await ReservationStatesAsync(factory, deliveryId, cancellationToken);
        Assert.AreEqual(4, states.Count, "四个维度必须在同一事务内原子预留并收敛。");
        Assert.IsTrue(states.All(state => state == expected), string.Join(",", states));
    }
}
