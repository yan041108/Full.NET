using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Features.ManageRecipientEndpoints;
using Full.NET.Modules.Notifications.Features.ReceiveProviderReceipts;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// Delivery Worker：Intent 后 accepted/Attempt=0，领取后 sent，回执 delivered，租约/崩溃/人工重试与权限。
/// </summary>
internal static class NotificationDeliveryWorkerAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var adminToken = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(
            client,
            cancellationToken);
        var hostUser = await NotificationProfileBindingAssertions.GetCurrentUserAsync(
            client,
            adminToken,
            cancellationToken);
        var harness = factory.Services.GetRequiredService<TestNotificationProviderHarness>();
        harness.Reset();

        var fixture = await CreateRouteAsync(client, adminToken, hostUser.Id, cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        harness.Reset();

        var accepted = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "A-1",
            cancellationToken);
        Assert.AreEqual(0, harness.SendCount);
        var delivery = await WaitDeliveryAsync(client, adminToken, accepted.Id, cancellationToken);
        Assert.AreEqual("accepted", delivery.StatusKey);
        Assert.AreEqual(0, delivery.Attempts.Count);

        harness.Reset(TestNotificationProviderMode.Succeed);
        var processed = await ProcessPendingAsync(factory, cancellationToken);
        Assert.IsTrue(processed >= 1);
        delivery = await GetDeliveryAsync(client, adminToken, delivery.Id, cancellationToken);
        Assert.AreEqual("sent", delivery.StatusKey);
        Assert.AreEqual(1, delivery.Attempts.Count);
        Assert.AreEqual("succeeded", delivery.Attempts[0].ResultCategoryKey);
        Assert.IsFalse(string.IsNullOrWhiteSpace(delivery.Attempts[0].ProviderMessageId));
        Assert.AreEqual(1, DistinctIdempotencyCount(harness));

        var otherProviderBody = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                receiptIdempotencyKey = $"other-provider-{delivery.Id:N}",
                providerMessageId = delivery.Attempts[0].ProviderMessageId,
                externalStatusKey = "delivered",
                mappedStatusKey = "delivered",
            }));
        using var otherProviderReceipt = await SendReceiptAsync(
            client,
            AlternateTestNotificationReceiptVerifier.ProviderTypeKeyValue,
            otherProviderBody,
            TestNotificationReceiptVerifier.Sign(otherProviderBody),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, otherProviderReceipt.StatusCode);
        delivery = await GetDeliveryAsync(client, adminToken, delivery.Id, cancellationToken);
        Assert.AreEqual(
            "sent",
            delivery.StatusKey,
            "不同 Provider 的相同外部消息号不得推进当前 Delivery。");

        var receiptBody = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                receiptIdempotencyKey = $"rcpt-{delivery.Id:N}",
                providerMessageId = delivery.Attempts[0].ProviderMessageId,
                externalStatusKey = "delivered",
                mappedStatusKey = "delivered",
            }));
        using var receiptResponse = await SendReceiptAsync(
            client,
            TestNotificationProvider.ProviderTypeKeyValue,
            receiptBody,
            TestNotificationReceiptVerifier.Sign(receiptBody),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, receiptResponse.StatusCode);
        var receipt = await receiptResponse.Content
            .ReadFromJsonAsync<NotificationReceiptAcceptedResponse>(cancellationToken);
        Assert.IsNotNull(receipt);
        Assert.AreEqual("processed", receipt.ProcessStatusKey);
        delivery = await GetDeliveryAsync(client, adminToken, delivery.Id, cancellationToken);
        Assert.AreEqual("delivered", delivery.StatusKey);

        using var duplicateReceipt = await SendReceiptAsync(
            client,
            TestNotificationProvider.ProviderTypeKeyValue,
            receiptBody,
            TestNotificationReceiptVerifier.Sign(receiptBody),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, duplicateReceipt.StatusCode);
        var duplicate = await duplicateReceipt.Content
            .ReadFromJsonAsync<NotificationReceiptAcceptedResponse>(cancellationToken);
        Assert.IsNotNull(duplicate);
        Assert.AreEqual("duplicate", duplicate.ProcessStatusKey);
        delivery = await GetDeliveryAsync(client, adminToken, delivery.Id, cancellationToken);
        Assert.AreEqual("delivered", delivery.StatusKey);

        var staleSentBody = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                receiptIdempotencyKey = $"stale-{delivery.Id:N}",
                providerMessageId = delivery.Attempts[0].ProviderMessageId,
                externalStatusKey = "sent",
                mappedStatusKey = "sent",
            }));
        using var staleResponse = await SendReceiptAsync(
            client,
            TestNotificationProvider.ProviderTypeKeyValue,
            staleSentBody,
            TestNotificationReceiptVerifier.Sign(staleSentBody),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, staleResponse.StatusCode);
        delivery = await GetDeliveryAsync(client, adminToken, delivery.Id, cancellationToken);
        Assert.AreEqual("delivered", delivery.StatusKey);

        using var badSignature = await SendReceiptAsync(
            client,
            TestNotificationProvider.ProviderTypeKeyValue,
            receiptBody,
            "00",
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, badSignature.StatusCode);
        var badSignatureBody = await badSignature.Content.ReadAsStringAsync(cancellationToken);
        NotificationProfileBindingAssertions.AssertProblem(
            badSignatureBody,
            NotificationsErrorCodes.ReceiptInvalid);
        Assert.IsFalse(badSignatureBody.Contains("test-msg-", StringComparison.Ordinal));

        using var unknownProvider = await SendReceiptAsync(
            client,
            "smtp.mailgun",
            receiptBody,
            TestNotificationReceiptVerifier.Sign(receiptBody),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, unknownProvider.StatusCode);
        NotificationProfileBindingAssertions.AssertProblem(
            await unknownProvider.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.ReceiptProviderUnknown);

        using var tooLarge = await SendReceiptAsync(
            client,
            TestNotificationProvider.ProviderTypeKeyValue,
            new byte[NotificationReceiptProcessor.MaxBodyBytes + 8],
            "00",
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        NotificationProfileBindingAssertions.AssertProblem(
            await tooLarge.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.ReceiptTooLarge);

        harness.Reset(TestNotificationProviderMode.Transient);
        var transientIntent = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "T-1",
            cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var transientDelivery = await WaitDeliveryAsync(
            client,
            adminToken,
            transientIntent.Id,
            cancellationToken);
        Assert.AreEqual("accepted", transientDelivery.StatusKey);
        Assert.AreEqual(1, transientDelivery.Attempts.Count);
        Assert.AreEqual("transient", transientDelivery.Attempts[0].ResultCategoryKey);
        Assert.IsNotNull(transientDelivery.NextAttemptAtUtc);

        harness.Reset(TestNotificationProviderMode.Permanent);
        var permanentIntent = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "P-1",
            cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var permanentDelivery = await WaitDeliveryAsync(
            client,
            adminToken,
            permanentIntent.Id,
            cancellationToken);
        Assert.AreEqual("failed", permanentDelivery.StatusKey);
        Assert.AreEqual(1, permanentDelivery.Attempts.Count);

        var readOnlyToken = await factory.CreateHostAccessTokenAsync(
            [NotificationPlatformPermissions.DeliveriesRead],
            cancellationToken);
        using var forbiddenRetry = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/deliveries/{permanentDelivery.Id:D}/retry",
            readOnlyToken,
            new RetryNotificationDeliveryRequest(permanentDelivery.Revision, "ops-retry"));
        using var forbiddenRetryResponse = await client.SendAsync(forbiddenRetry, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenRetryResponse.StatusCode);

        using var retry = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/deliveries/{permanentDelivery.Id:D}/retry",
            adminToken,
            new RetryNotificationDeliveryRequest(permanentDelivery.Revision, "ops-retry"));
        using var retryResponse = await client.SendAsync(retry, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, retryResponse.StatusCode);
        var retried = await retryResponse.Content
            .ReadFromJsonAsync<NotificationDeliveryResponse>(cancellationToken);
        Assert.IsNotNull(retried);
        Assert.AreEqual("accepted", retried.StatusKey);
        harness.Reset(TestNotificationProviderMode.Succeed);
        await ProcessPendingAsync(factory, cancellationToken);
        retried = await GetDeliveryAsync(client, adminToken, retried.Id, cancellationToken);
        Assert.AreEqual("sent", retried.StatusKey);

        harness.Reset(TestNotificationProviderMode.Crash);
        var crashIntent = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "C-1",
            cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var crashDelivery = await WaitDeliveryAsync(client, adminToken, crashIntent.Id, cancellationToken);
        Assert.AreEqual("accepted", crashDelivery.StatusKey);
        Assert.AreEqual(0, crashDelivery.Attempts.Count);
        Assert.AreEqual(1, DistinctIdempotencyCount(harness));
        await ExpireLeaseAsync(factory, crashDelivery.Id, cancellationToken);
        harness.Mode = TestNotificationProviderMode.Succeed;
        await ProcessPendingAsync(factory, cancellationToken);
        crashDelivery = await GetDeliveryAsync(client, adminToken, crashDelivery.Id, cancellationToken);
        Assert.AreEqual("sent", crashDelivery.StatusKey);
        Assert.AreEqual(1, crashDelivery.Attempts.Count);
        Assert.AreEqual(1, DistinctIdempotencyCount(harness));

        harness.Reset(TestNotificationProviderMode.Succeed);
        var concurrentIntent = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "X-1",
            cancellationToken);
        var concurrentDelivery = await WaitDeliveryAsync(
            client,
            adminToken,
            concurrentIntent.Id,
            cancellationToken);
        await Task.WhenAll(
            ProcessPendingAsync(factory, cancellationToken),
            ProcessPendingAsync(factory, cancellationToken));
        concurrentDelivery = await GetDeliveryAsync(
            client,
            adminToken,
            concurrentDelivery.Id,
            cancellationToken);
        Assert.AreEqual("sent", concurrentDelivery.StatusKey);
        Assert.AreEqual(1, concurrentDelivery.Attempts.Count);

        harness.Reset(TestNotificationProviderMode.Slow);
        harness.SlowDelay = TimeSpan.FromMilliseconds(700);
        var slowIntent = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "S-1",
            cancellationToken);
        var first = ProcessPendingAsync(factory, cancellationToken);
        await Task.Delay(80, cancellationToken);
        var secondWatch = Stopwatch.StartNew();
        await ProcessPendingAsync(factory, cancellationToken);
        secondWatch.Stop();
        Assert.IsTrue(secondWatch.ElapsedMilliseconds < 500);
        await first;
        var slowDelivery = await WaitDeliveryAsync(client, adminToken, slowIntent.Id, cancellationToken);
        Assert.AreEqual("sent", slowDelivery.StatusKey);

        using var tenantClient = factory.CreateClientForHost("localhost");
        var tenantSwitch = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(
            tenantClient,
            cancellationToken);
        var tenantToken = await NotificationProfileBindingAssertions.EnterAcmeTenantAsync(
            tenantClient,
            tenantSwitch,
            cancellationToken);
        using var tenantGet = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/notifications/deliveries/{delivery.Id:D}");
        tenantGet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantToken);
        using var tenantGetResponse = await tenantClient.SendAsync(tenantGet, cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, tenantGetResponse.StatusCode);

        await VerifyProtectedRecipientEndpointAsync(
            factory,
            client,
            adminToken,
            hostUser.Id,
            cancellationToken);

        await OpenApiNotificationsDeliveriesReceiptsContractAssertions.VerifyAsync(
            client,
            cancellationToken);
    }

    private static async Task VerifyProtectedRecipientEndpointAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        const string secretReference = "vault://test/smtp-password";
        const string rawEmail = "recipient@example.test";
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(
            client,
            token,
            $"ep-{Guid.NewGuid():N}"[..16],
            TestEndpointNotificationProvider.ProviderTypeKeyValue,
            new { endpointBaseUrl = "https://smtp.test" },
            secretReference,
            cancellationToken);
        profile = await NotificationProfileBindingAssertions.PublishAndEnableAsync(
            client,
            token,
            profile,
            cancellationToken);
        Assert.IsNotNull(profile.LatestPublishedVersionId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var endpointStore = scope.ServiceProvider.GetRequiredService<RecipientEndpointStore>();
            var stored = await endpointStore.UpsertAsync(
                recipientUserId,
                profile.LatestPublishedVersionId.Value,
                TestEndpointNotificationProvider.EndpointKindKey,
                rawEmail,
                NotificationRecipientEndpointStatuses.Verified,
                cancellationToken);
            Assert.IsTrue(stored.IsSuccess);
        }

        var producer = $"tests.endpoint.{Guid.NewGuid():N}"[..28];
        const string scene = "order.email";
        await NotificationProfileBindingAssertions.CreateAndPublishBindingAsync(
            client,
            token,
            $"eb-{Guid.NewGuid():N}"[..16],
            producer,
            scene,
            profile.ProfileKey,
            TestEndpointNotificationProvider.ChannelKey,
            cancellationToken);
        var templateKey = $"et-{Guid.NewGuid():N}"[..16];
        await NotificationProfileBindingAssertions.CreateAndPublishTestTemplateAsync(
            client,
            token,
            templateKey,
            TestEndpointNotificationProvider.ChannelKey,
            cancellationToken);

        var harness = factory.Services.GetRequiredService<TestEndpointNotificationProviderHarness>();
        harness.Reset();
        var intent = await CreateIntentAsync(
            client,
            token,
            new RouteFixture(producer, scene, templateKey),
            recipientUserId,
            "E-1",
            cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var delivery = await WaitDeliveryAsync(client, token, intent.Id, cancellationToken);
        Assert.AreEqual("sent", delivery.StatusKey);
        var providerRequest = harness.Requests.Single();
        Assert.AreEqual(rawEmail, providerRequest.RecipientEndpoint);
        Assert.AreEqual("{\"endpointBaseUrl\":\"https://smtp.test\"}", providerRequest.NonSecretConfigJson);
        Assert.AreEqual(secretReference, providerRequest.SecretReference);
        Assert.IsFalse(providerRequest.RecipientEndpoint.Contains(recipientUserId.ToString("N"), StringComparison.Ordinal));
    }

    private static async Task<RouteFixture> CreateRouteAsync(
        HttpClient client,
        string token,
        Guid recipientUserId,
        CancellationToken cancellationToken)
    {
        _ = recipientUserId;
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(
            client,
            token,
            $"del-{Guid.NewGuid():N}"[..16],
            cancellationToken);
        profile = await NotificationProfileBindingAssertions.PublishAndEnableAsync(
            client,
            token,
            profile,
            cancellationToken);
        var producer = $"tests.delivery.{Guid.NewGuid():N}"[..28];
        var scene = "order.shipped";
        var binding = await NotificationProfileBindingAssertions.CreateAndPublishBindingAsync(
            client,
            token,
            $"db-{Guid.NewGuid():N}"[..16],
            producer,
            scene,
            profile.ProfileKey,
            cancellationToken);
        var templateKey = $"dt-{Guid.NewGuid():N}"[..16];
        await NotificationProfileBindingAssertions.CreateAndPublishTestTemplateAsync(
            client,
            token,
            templateKey,
            cancellationToken);
        return new RouteFixture(producer, scene, templateKey);
    }

    private static async Task<NotificationIntentResponse> CreateIntentAsync(
        HttpClient client,
        string token,
        RouteFixture fixture,
        Guid recipientUserId,
        string orderNo,
        CancellationToken cancellationToken)
    {
        using var request = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            token,
            new
            {
                producerKey = fixture.Producer,
                sceneKey = fixture.Scene,
                templateKey = fixture.TemplateKey,
                recipients = new[]
                {
                    new { recipientTypeKey = "user", recipientKey = recipientUserId.ToString("N") },
                },
                parameters = new { orderNo },
                idempotencyKey = $"idem-{Guid.NewGuid():N}",
            });
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            response.StatusCode,
            await response.Content.ReadAsStringAsync(cancellationToken));
        var intent = await response.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken);
        Assert.IsNotNull(intent);
        return intent;
    }

    private static async Task<NotificationDeliveryResponse> WaitDeliveryAsync(
        HttpClient client,
        string token,
        Guid intentId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/notifications/deliveries?page=1&pageSize=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var items = document.RootElement.GetProperty("items");
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("intentId").GetGuid() == intentId)
            {
                var id = item.GetProperty("id").GetGuid();
                return await GetDeliveryAsync(client, token, id, cancellationToken);
            }
        }

        Assert.Fail($"未找到 Intent {intentId:D} 的 Delivery。");
        throw new InvalidOperationException("unreachable");
    }

    private static async Task<NotificationDeliveryResponse> GetDeliveryAsync(
        HttpClient client,
        string token,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/v1/notifications/deliveries/{deliveryId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var delivery = await response.Content
            .ReadFromJsonAsync<NotificationDeliveryResponse>(cancellationToken);
        Assert.IsNotNull(delivery);
        Assert.IsFalse(
            JsonSerializer.Serialize(delivery).Contains("secret", StringComparison.OrdinalIgnoreCase));
        return delivery;
    }

    private static async Task<int> ProcessPendingAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>();
        return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExpireLeaseAsync(
        FullNetApiFactory factory,
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        await commandExecutor.ExecuteAsync(
                NotificationPlatformSql.ExpireDeliveryLease,
                NotificationPlatformSqlParameters.Create(
                    ("Id", deliveryId),
                    ("ExpiredAt", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero))),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> SendReceiptAsync(
        HttpClient client,
        string providerTypeKey,
        byte[] body,
        string signature,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/notifications/provider-receipts/{providerTypeKey}")
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(TestNotificationReceiptVerifier.SignatureHeaderName, signature);
        return await client.SendAsync(request, cancellationToken);
    }

    private static int DistinctIdempotencyCount(TestNotificationProviderHarness harness) =>
        harness.IdempotencyKeys.Distinct(StringComparer.Ordinal).Count();

    private sealed record RouteFixture(string Producer, string Scene, string TemplateKey);
}
