using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Features.ManageRecipientEndpoints;
using Full.NET.Modules.Notifications.Features.ReceiveProviderReceipts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers.AliyunSms;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// Delivery Worker：Intent 后 accepted/Attempt=0，领取后 sent，回执 delivered，租约/崩溃/人工重试与权限。
/// </summary>
internal static class NotificationDeliveryWorkerAssertions
{
    /// <summary>同一数据库和提供程序延迟下比较串行与四路执行，并验证每条终态只提交一次。</summary>
    internal static async Task VerifyConcurrencyAsync(FullNetApiFactory factory, Action<string> evidence, Action? advanceClock = null)
    {
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, default);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, default);
        var fixture = await CreateRouteAsync(client, token, user.Id, default);
        await ProcessPendingAsync(factory, default);
        var harness = factory.Services.GetRequiredService<TestNotificationProviderHarness>();
        foreach (var scenario in new[] { (Concurrency: 1, LeaseStress: false), (Concurrency: 4, LeaseStress: false), (Concurrency: 1, LeaseStress: true) })
        {
            if (scenario.LeaseStress && advanceClock is null) continue;
            var concurrency = scenario.Concurrency;
            factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationDeliveryWorkerOptions>>().Value.MaxConcurrency = concurrency;
            var deliveries = new List<NotificationDeliveryResponse>();
            for (var i = 0; i < 8; i++)
            {
                var intent = await CreateIntentAsync(client, token, fixture, user.Id, $"parallel-{concurrency}-{scenario.LeaseStress}-{i}", default);
                deliveries.Add(await WaitDeliveryAsync(client, token, intent.Id, default));
            }
            harness.Reset(TestNotificationProviderMode.Slow);
            harness.SlowDelay = scenario.LeaseStress ? TimeSpan.Zero : TimeSpan.FromMilliseconds(150);
            harness.OnSend = scenario.LeaseStress ? advanceClock : null;
            var watch = Stopwatch.StartNew();
            Assert.AreEqual(8, await ProcessPendingAsync(factory, default));
            watch.Stop();
            Assert.AreEqual(8, harness.SendCount);
            Assert.AreEqual(8, DistinctIdempotencyCount(harness));
            Assert.IsTrue(harness.PeakConcurrency <= concurrency);
            Assert.IsTrue(concurrency == 1 ? harness.PeakConcurrency == 1 : harness.PeakConcurrency > 1);
            foreach (var item in deliveries)
            {
                var result = await GetDeliveryAsync(client, token, item.Id, default);
                Assert.AreEqual("sent", result.StatusKey);
                Assert.AreEqual(1, result.Attempts.Count);
            }
            Assert.AreEqual(0, await ProcessPendingAsync(factory, default));
            evidence($"notifications concurrency={concurrency} lease_stress={scenario.LeaseStress} count=8 peak={harness.PeakConcurrency} elapsed_ms={watch.Elapsed.TotalMilliseconds:F2} errors=0 duplicates=0");
        }
        harness.OnSend = null;
    }

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
        Assert.AreEqual("completed", receipt.ProcessStatusKey);
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
        // 迟到的 sent 回执晚于 delivered 到达：状态机拒绝回退，但必须被可审计地记为 ignored，
        // 而不是静默丢弃，也不能把已送达的 Delivery 拉回 sent。
        var staleAccepted = await staleResponse.Content
            .ReadFromJsonAsync<NotificationReceiptAcceptedResponse>(cancellationToken);
        Assert.IsNotNull(staleAccepted);
        Assert.AreEqual(
            "ignored",
            staleAccepted.ProcessStatusKey,
            "乱序迟到的 sent 回执必须被可审计地记为 ignored，而不是静默丢弃。");
        delivery = await GetDeliveryAsync(client, adminToken, delivery.Id, cancellationToken);
        Assert.AreEqual("delivered", delivery.StatusKey);

        // 并发重复回执：同一幂等键被同时投递两次时，必须恰好推进一次、只入库一条回执，
        // 另一路幂等返回 duplicate；不得出现重复回执行，也不得吞掉未完成工作。
        harness.Reset(TestNotificationProviderMode.Succeed);
        var concurrentReceiptIntent = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "RC-1",
            cancellationToken);
        await ProcessPendingAsync(factory, cancellationToken);
        var concurrentReceiptDelivery = await WaitDeliveryAsync(
            client,
            adminToken,
            concurrentReceiptIntent.Id,
            cancellationToken);
        Assert.AreEqual("sent", concurrentReceiptDelivery.StatusKey);
        var concurrentReceiptBody = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                receiptIdempotencyKey = $"rcpt-concurrent-{concurrentReceiptDelivery.Id:N}",
                providerMessageId = concurrentReceiptDelivery.Attempts[0].ProviderMessageId,
                externalStatusKey = "delivered",
                mappedStatusKey = "delivered",
            }));
        var concurrentReceiptSignature = TestNotificationReceiptVerifier.Sign(concurrentReceiptBody);
        var concurrentReceiptResponses = await Task.WhenAll(
            SendReceiptAsync(
                client,
                TestNotificationProvider.ProviderTypeKeyValue,
                concurrentReceiptBody,
                concurrentReceiptSignature,
                cancellationToken),
            SendReceiptAsync(
                client,
                TestNotificationProvider.ProviderTypeKeyValue,
                concurrentReceiptBody,
                concurrentReceiptSignature,
                cancellationToken));
        var concurrentReceiptStatuses = new List<string>();
        foreach (var concurrentReceiptResponse in concurrentReceiptResponses)
        {
            using (concurrentReceiptResponse)
            {
                Assert.AreEqual(
                    HttpStatusCode.OK,
                    concurrentReceiptResponse.StatusCode,
                    await concurrentReceiptResponse.Content.ReadAsStringAsync(cancellationToken));
                var concurrentAccepted = await concurrentReceiptResponse.Content
                    .ReadFromJsonAsync<NotificationReceiptAcceptedResponse>(cancellationToken);
                Assert.IsNotNull(concurrentAccepted);
                concurrentReceiptStatuses.Add(concurrentAccepted.ProcessStatusKey);
            }
        }

        Assert.AreEqual(
            1,
            concurrentReceiptStatuses.Count(status => status == "completed"),
            "同一幂等键的并发回执必须恰好推进一次。");
        concurrentReceiptDelivery = await GetDeliveryAsync(
            client,
            adminToken,
            concurrentReceiptDelivery.Id,
            cancellationToken);
        Assert.AreEqual("delivered", concurrentReceiptDelivery.StatusKey);
        Assert.AreEqual(
            1,
            concurrentReceiptDelivery.Receipts.Count,
            "同一幂等键的并发回执只应入库一条。");

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

        using var smtpReceipt = await SendReceiptAsync(
            client,
            "email.smtp",
            receiptBody,
            TestNotificationReceiptVerifier.Sign(receiptBody),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.NotFound, smtpReceipt.StatusCode);
        NotificationProfileBindingAssertions.AssertProblem(
            await smtpReceipt.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.ReceiptNotSupported);

        Environment.SetEnvironmentVariable("FULLNET_TEST_ALIYUN_SMS_RECEIPT_SECRET", "integration-receipt-secret");
        var aliyunReceiptBody = Encoding.UTF8.GetBytes(
            """
            [
              {
                "phone_number": "13800138000",
                "send_time": "2026-09-06 10:00:00",
                "report_time": "2026-09-06 10:00:01",
                "success": true,
                "err_code": "DELIVERED",
                "biz_id": "aliyun-biz-unmatched"
              }
            ]
            """);
        var aliyunSignature = AliyunSmsReceiptVerifier.Sign(
            aliyunReceiptBody,
            "integration-receipt-secret");
        using var aliyunReceipt = await SendReceiptAsync(
            client,
            "sms.aliyun",
            aliyunReceiptBody,
            aliyunSignature,
            cancellationToken,
            AliyunSmsReceiptVerifier.SignatureHeaderName);
        Assert.AreEqual(HttpStatusCode.OK, aliyunReceipt.StatusCode);
        Environment.SetEnvironmentVariable("FULLNET_TEST_ALIYUN_SMS_RECEIPT_SECRET", null);

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

        // 持续瞬时异常必须逐次消耗重试预算并在到达 MaxAttempts 后进入死信，不能无限重试。
        // 这里缩小预算与退避，既验证预算耗尽路径，又避免真实等待数秒。
        var workerOptions = factory.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<NotificationDeliveryWorkerOptions>>()
            .Value;
        var previousMaxAttempts = workerOptions.MaxAttempts;
        var previousRetryDelaySeconds = workerOptions.RetryDelaySeconds;
        var previousRetryBackoffMode = workerOptions.RetryBackoffMode;
        workerOptions.MaxAttempts = 3;
        workerOptions.RetryDelaySeconds = 1;
        workerOptions.RetryBackoffMode = "fixed";
        try
        {
            harness.Reset(TestNotificationProviderMode.Transient);
            var budgetIntent = await CreateIntentAsync(
                client,
                adminToken,
                fixture,
                hostUser.Id,
                "B-1",
                cancellationToken);
            await ProcessPendingAsync(factory, cancellationToken);
            var budgetDelivery = await WaitDeliveryAsync(
                client,
                adminToken,
                budgetIntent.Id,
                cancellationToken);
            Assert.AreEqual("accepted", budgetDelivery.StatusKey);
            for (var round = 0; round < 2; round++)
            {
                Assert.IsNotNull(
                    budgetDelivery.NextAttemptAtUtc,
                    "未到达上限前的瞬时异常必须安排下一次领取。");
                await Task.Delay(TimeSpan.FromSeconds(1.2), cancellationToken);
                await ProcessPendingAsync(factory, cancellationToken);
                budgetDelivery = await GetDeliveryAsync(
                    client,
                    adminToken,
                    budgetDelivery.Id,
                    cancellationToken);
            }

            Assert.AreEqual(
                "dead_lettered",
                budgetDelivery.StatusKey,
                "持续异常到达 MaxAttempts 必须进入死信而不是无限重试。");
            Assert.IsNull(budgetDelivery.NextAttemptAtUtc);
            Assert.AreEqual(3, budgetDelivery.Attempts.Count);

            // 死信不参与自动领取，即使提供程序恢复也不会被重新发送。
            harness.Mode = TestNotificationProviderMode.Succeed;
            await ProcessPendingAsync(factory, cancellationToken);
            budgetDelivery = await GetDeliveryAsync(
                client,
                adminToken,
                budgetDelivery.Id,
                cancellationToken);
            Assert.AreEqual("dead_lettered", budgetDelivery.StatusKey);
            Assert.AreEqual(3, budgetDelivery.Attempts.Count);
        }
        finally
        {
            workerOptions.MaxAttempts = previousMaxAttempts;
            workerOptions.RetryDelaySeconds = previousRetryDelaySeconds;
            workerOptions.RetryBackoffMode = previousRetryBackoffMode;
        }

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

        // 崩溃发生在外部调用启动之后：必须计入尝试次数并暂挂为 unknown，等待回执对账或人工处置，
        // 不能因为“异常后不写 Attempt”而无限绕过 MaxAttempts。
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
        Assert.AreEqual("unknown", crashDelivery.StatusKey);
        Assert.AreEqual(1, crashDelivery.Attempts.Count);
        Assert.AreEqual("unknown", crashDelivery.Attempts[0].ResultCategoryKey);
        Assert.IsNull(crashDelivery.NextAttemptAtUtc);
        Assert.AreEqual(1, harness.IdempotencyKeysFor(crashDelivery.Id).Distinct(StringComparer.Ordinal).Count());

        // 非幂等渠道的暂挂 unknown 不参与自动领取：即使提供程序恢复正常也不会自动重发。
        harness.Mode = TestNotificationProviderMode.Succeed;
        await ProcessPendingAsync(factory, cancellationToken);
        crashDelivery = await GetDeliveryAsync(client, adminToken, crashDelivery.Id, cancellationToken);
        Assert.AreEqual("unknown", crashDelivery.StatusKey, "暂挂的 unknown 不得被自动领取重发。");
        Assert.AreEqual(1, crashDelivery.Attempts.Count);

        // 人工重投不能把操作理由当作可信对账结果，旧世代未决配额仍阻止再次外发。
        using var crashRetry = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/deliveries/{crashDelivery.Id:D}/retry",
            adminToken,
            new RetryNotificationDeliveryRequest(crashDelivery.Revision, "ops-reconcile-unknown"));
        using var crashRetryResponse = await client.SendAsync(crashRetry, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, crashRetryResponse.StatusCode);
        await ProcessPendingAsync(factory, cancellationToken);
        crashDelivery = await GetDeliveryAsync(client, adminToken, crashDelivery.Id, cancellationToken);
        Assert.AreEqual("unknown", crashDelivery.StatusKey);
        Assert.IsNull(crashDelivery.NextAttemptAtUtc);
        Assert.AreEqual(1, crashDelivery.Attempts.Count);
        var crashKeys = harness.IdempotencyKeysFor(crashDelivery.Id);
        Assert.AreEqual(1, crashKeys.Count, "人工重投不得绕过旧世代未决额度再次调用厂商。");

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

        harness.Reset(TestNotificationProviderMode.Succeed);
        var slowIntent = await CreateIntentAsync(
            client,
            adminToken,
            fixture,
            hostUser.Id,
            "S-1",
            cancellationToken);
        var slowDelivery = await WaitDeliveryAsync(client, adminToken, slowIntent.Id, cancellationToken);
        var sendEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSendAsync = async (request, token) =>
        {
            if (request.DeliveryId != slowDelivery.Id) return;
            sendEntered.TrySetResult();
            await releaseSend.Task.WaitAsync(token);
        };
        // 用可控外发阻塞证明第二个 Worker 能结束，不把本机调度速度当作事务边界的证据。
        var first = ProcessPendingAsync(factory, cancellationToken);
        try
        {
            await sendEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            await ProcessPendingAsync(factory, cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            Assert.IsFalse(first.IsCompleted, "第二个 Worker 完成时，首个厂商调用必须仍被受控信号阻塞。");
            Assert.AreEqual(1, harness.IdempotencyKeysFor(slowDelivery.Id).Count);
        }
        finally
        {
            releaseSend.TrySetResult();
            await first;
            harness.OnSendAsync = null;
        }
        slowDelivery = await GetDeliveryAsync(client, adminToken, slowDelivery.Id, cancellationToken);
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
        // 受控目录缺失会让意图受理失败关闭，用例必须显式声明分类与合法渠道。
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(
            client,
            token,
            producer,
            scene,
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

    internal static async Task<RouteFixture> CreateRouteAsync(
        HttpClient client,
        string token,
        Guid recipientUserId,
        CancellationToken cancellationToken,
        string policyCategoryKey = "transactional")
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
        Assert.IsNotNull(binding.LatestPublishedVersionId);
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(
            client,
            token,
            producer,
            scene,
            TestNotificationProvider.ChannelKey,
            cancellationToken,
            policyCategoryKey);
        var templateKey = $"dt-{Guid.NewGuid():N}"[..16];
        await NotificationProfileBindingAssertions.CreateAndPublishTestTemplateAsync(
            client,
            token,
            templateKey,
            cancellationToken);
        return new RouteFixture(producer, scene, templateKey);
    }

    internal static async Task<NotificationIntentResponse> CreateIntentAsync(
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

    internal static async Task<NotificationDeliveryResponse> WaitDeliveryAsync(
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

    internal static async Task<NotificationDeliveryResponse> GetDeliveryAsync(
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

    internal static async Task<int> ProcessPendingAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>();
        return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<HttpResponseMessage> SendReceiptAsync(
        HttpClient client,
        string providerTypeKey,
        byte[] body,
        string signature,
        CancellationToken cancellationToken,
        string? signatureHeaderName = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/notifications/provider-receipts/{providerTypeKey}")
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(
            signatureHeaderName ?? TestNotificationReceiptVerifier.SignatureHeaderName,
            signature);
        return await client.SendAsync(request, cancellationToken);
    }

    private static int DistinctIdempotencyCount(TestNotificationProviderHarness harness) =>
        harness.IdempotencyKeys.Distinct(StringComparer.Ordinal).Count();

    internal sealed record RouteFixture(string Producer, string Scene, string TemplateKey);
}
