using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// 批次一投递正确性端到端断言：Failover 只物化首选且仅允许失败才原子推进备用、
/// Match 受控条件唯一命中求值，以及提前到达回执在 Attempt 提交后的有界补偿重关联。
/// </summary>
/// <remarks>
/// 这些场景在 SQL Server 与 MySQL 上以相同用例执行，用于证明首选成功不发备用、
/// 永久错误与结果未知不换厂商、Match 无命中/多命中失败关闭，以及提前回执不会永久停留在待关联。
/// </remarks>
internal static class NotificationRoutingFailoverAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, default);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, default);
        var harness = factory.Services.GetRequiredService<TestNotificationProviderHarness>();
        // 结果未知的自动重试默认失败关闭，确保 Unknown 场景验证的是暂挂而非自动重发。
        harness.AllowsAutomaticRetryAfterUnknownResult = false;

        var primary = await CreateEnabledProfileAsync(client, token, $"fo-a-{Guid.NewGuid():N}"[..20], default);
        var secondary = await CreateEnabledProfileAsync(client, token, $"fo-b-{Guid.NewGuid():N}"[..20], default);

        await VerifyFailoverAsync(factory, client, token, user.Id, harness, primary, secondary, default);
        await VerifyConcurrentFailoverAsync(factory, client, token, user.Id, harness, primary, secondary, default);
        await VerifyMatchAsync(client, token, user.Id, primary, secondary, default);
        await VerifyEarlyReceiptReconciliationAsync(factory, client, token, user.Id, primary, default);
    }

    /// <summary>
    /// Failover：首选成功时只发首选、不创建备用；仅允许的瞬时失败才原子推进备用；
    /// 永久错误与结果未知都不直接换厂商。
    /// </summary>
    private static async Task VerifyFailoverAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid recipientUserId,
        TestNotificationProviderHarness harness,
        NotificationProviderProfileResponse primary,
        NotificationProviderProfileResponse secondary,
        CancellationToken cancellationToken)
    {
        var producer = $"tests.failover.{Guid.NewGuid():N}"[..28];
        const string scene = "order.shipped";
        var binding = await CreateAndPublishBindingAsync(
            client,
            token,
            $"fb-{Guid.NewGuid():N}"[..16],
            producer,
            scene,
            "failover",
            TestNotificationProvider.ChannelKey,
            [
                new { profileKey = primary.ProfileKey, order = 1 },
                new { profileKey = secondary.ProfileKey, order = 2 },
            ],
            cancellationToken);
        Assert.IsNotNull(binding.LatestPublishedVersionId);
        // 分类与合法渠道只能来自受控目录，缺失声明时受理失败关闭。
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(
            client,
            token,
            producer,
            scene,
            TestNotificationProvider.ChannelKey,
            cancellationToken);
        var templateKey = await CreateTemplateAsync(
            client,
            token,
            $"ft-{Guid.NewGuid():N}"[..16],
            cancellationToken);

        // 首选成功：只物化首选目标，备用不会同时产生投递。
        var preferred = await CreateIntentAsync(
            client, token, producer, scene, templateKey, recipientUserId, new { orderNo = "F-1" }, cancellationToken);
        var preferredDeliveries = await ListDeliveriesForIntentAsync(client, token, preferred.Id, cancellationToken);
        Assert.AreEqual(1, preferredDeliveries.Count, "Failover 首次只应物化首选目标。");
        Assert.AreEqual(primary.LatestPublishedVersionId, preferredDeliveries[0].ProviderProfileVersionId);

        harness.Reset(TestNotificationProviderMode.Succeed);
        await DrainPendingAsync(factory, cancellationToken);
        var preferredAfter = await GetDeliveryAsync(client, token, preferredDeliveries[0].Id, cancellationToken);
        Assert.AreEqual("sent", preferredAfter.StatusKey);
        Assert.AreEqual(1, preferredAfter.Attempts.Count);
        Assert.AreEqual(1, harness.SendCount, "首选成功后不得再发送备用目标。");
        Assert.AreEqual(
            1,
            (await ListDeliveriesForIntentAsync(client, token, preferred.Id, cancellationToken)).Count,
            "首选成功后不得创建备用投递。");

        // 允许的瞬时失败：首选进入失败终态，原子推进备用并只发送一次。
        var sends = 0;
        harness.Reset(TestNotificationProviderMode.Transient);
        harness.OnSend = () =>
        {
            if (Interlocked.Increment(ref sends) > 1)
            {
                harness.Mode = TestNotificationProviderMode.Succeed;
            }
        };
        var failover = await CreateIntentAsync(
            client, token, producer, scene, templateKey, recipientUserId, new { orderNo = "F-2" }, cancellationToken);
        await DrainPendingAsync(factory, cancellationToken);
        harness.OnSend = null;

        var failoverDeliveries = await ListDeliveriesForIntentAsync(client, token, failover.Id, cancellationToken);
        Assert.AreEqual(2, failoverDeliveries.Count, "瞬时失败后才应推进备用目标。");
        var failedPrimary = failoverDeliveries.Single(item => item.ProviderProfileVersionId == primary.LatestPublishedVersionId);
        var activatedBackup = failoverDeliveries.Single(item => item.ProviderProfileVersionId == secondary.LatestPublishedVersionId);
        Assert.AreEqual("failed", failedPrimary.StatusKey);
        Assert.AreEqual("transient", failedPrimary.Attempts.Single().ResultCategoryKey);
        Assert.AreEqual("sent", activatedBackup.StatusKey);
        Assert.AreEqual(2, harness.SendCount, "瞬时失败换厂商只应各发送一次。");

        // 永久错误：不换厂商，只保留首选失败终态。
        harness.Reset(TestNotificationProviderMode.Permanent);
        var permanent = await CreateIntentAsync(
            client, token, producer, scene, templateKey, recipientUserId, new { orderNo = "F-3" }, cancellationToken);
        await DrainPendingAsync(factory, cancellationToken);
        var permanentDeliveries = await ListDeliveriesForIntentAsync(client, token, permanent.Id, cancellationToken);
        Assert.AreEqual(1, permanentDeliveries.Count, "永久错误不得换厂商。");
        Assert.AreEqual("failed", permanentDeliveries[0].StatusKey);

        // 结果未知：渠道无法证明不重复发送时暂挂，不换厂商也不自动重发。
        harness.Reset(TestNotificationProviderMode.Unknown);
        var unknown = await CreateIntentAsync(
            client, token, producer, scene, templateKey, recipientUserId, new { orderNo = "F-4" }, cancellationToken);
        await DrainPendingAsync(factory, cancellationToken);
        var unknownDeliveries = await ListDeliveriesForIntentAsync(client, token, unknown.Id, cancellationToken);
        Assert.AreEqual(1, unknownDeliveries.Count, "结果未知不得换厂商。");
        Assert.AreEqual("unknown", unknownDeliveries[0].StatusKey);
        Assert.IsNull(unknownDeliveries[0].NextAttemptAtUtc);

        harness.Reset(TestNotificationProviderMode.Succeed);
    }

    /// <summary>
    /// 双 Worker 竞争：租约必须保证同一投递只被一个 Worker 发送，故障转移只原子激活一条备用投递，
    /// 不能因并发领取而重复物化备用目标或重复发送。
    /// </summary>
    private static async Task VerifyConcurrentFailoverAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid recipientUserId,
        TestNotificationProviderHarness harness,
        NotificationProviderProfileResponse primary,
        NotificationProviderProfileResponse secondary,
        CancellationToken cancellationToken)
    {
        var producer = $"tests.failover.{Guid.NewGuid():N}"[..28];
        const string scene = "order.race";
        await CreateAndPublishBindingAsync(
            client,
            token,
            $"cb-{Guid.NewGuid():N}"[..16],
            producer,
            scene,
            "failover",
            TestNotificationProvider.ChannelKey,
            [
                new { profileKey = primary.ProfileKey, order = 1 },
                new { profileKey = secondary.ProfileKey, order = 2 },
            ],
            cancellationToken);
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(
            client,
            token,
            producer,
            scene,
            TestNotificationProvider.ChannelKey,
            cancellationToken);
        var templateKey = await CreateTemplateAsync(
            client,
            token,
            $"ct-{Guid.NewGuid():N}"[..16],
            cancellationToken);

        // 首次发送保持瞬时失败以触发故障转移，第二次发送切换到成功，避免备用自身失败干扰并发断言。
        var sends = 0;
        harness.Reset(TestNotificationProviderMode.Transient);
        harness.OnSend = () =>
        {
            if (Interlocked.Increment(ref sends) > 1)
            {
                harness.Mode = TestNotificationProviderMode.Succeed;
            }
        };

        var raced = await CreateIntentAsync(
            client, token, producer, scene, templateKey, recipientUserId, new { orderNo = "C-1" }, cancellationToken);
        await Task.WhenAll(
            ProcessPendingOnceAsync(factory, cancellationToken),
            ProcessPendingOnceAsync(factory, cancellationToken));
        harness.OnSend = null;

        var racedDeliveries = await ListDeliveriesForIntentAsync(client, token, raced.Id, cancellationToken);
        Assert.AreEqual(
            1,
            racedDeliveries.Count(item => item.ProviderProfileVersionId == primary.LatestPublishedVersionId),
            "首选目标只允许物化一条投递。");
        Assert.AreEqual(
            1,
            racedDeliveries.Count(item => item.ProviderProfileVersionId == secondary.LatestPublishedVersionId),
            "双 Worker 竞争只允许激活一条备用投递。");
        var racedFailed = racedDeliveries.Single(
            item => item.ProviderProfileVersionId == primary.LatestPublishedVersionId);
        Assert.AreEqual("failed", racedFailed.StatusKey);
        Assert.AreEqual(
            1,
            racedFailed.Attempts.Count,
            "同一投递在双 Worker 竞争下只允许产生一次发送尝试。");

        harness.Mode = TestNotificationProviderMode.Succeed;
        await DrainPendingAsync(factory, cancellationToken);
        racedDeliveries = await ListDeliveriesForIntentAsync(client, token, raced.Id, cancellationToken);
        Assert.AreEqual(2, racedDeliveries.Count, "故障转移只允许存在首选与备用两条投递。");
        foreach (var delivery in racedDeliveries)
        {
            Assert.AreEqual(1, delivery.Attempts.Count, "每个目标都只能发送一次。");
        }

        Assert.AreEqual("sent", racedDeliveries
            .Single(item => item.ProviderProfileVersionId == secondary.LatestPublishedVersionId).StatusKey);
        Assert.AreEqual(2, harness.SendCount, "首选与备用各发送一次，共两次。");
        harness.Reset(TestNotificationProviderMode.Succeed);
    }

    /// <summary>
    /// Match：受控条件唯一命中才生成投递；无命中与多命中都失败关闭；缺失条件在发布时被拒绝。
    /// </summary>
    private static async Task VerifyMatchAsync(
        HttpClient client,
        string token,
        Guid recipientUserId,
        NotificationProviderProfileResponse primary,
        NotificationProviderProfileResponse secondary,
        CancellationToken cancellationToken)
    {
        var producer = $"tests.match.{Guid.NewGuid():N}"[..28];
        const string scene = "order.routed";
        // 条件引用模板参数 channel，因此显式声明该参数，保证条件对真实参数快照求值。
        var routedTemplateKey = await CreateTemplateAsync(
            client,
            token,
            $"rc-{Guid.NewGuid():N}"[..16],
            cancellationToken,
            parameters: [
                new { name = "orderNo", typeKey = "string", required = false, maxLength = 32 },
                new { name = "channel", typeKey = "string", required = true, maxLength = 32 },
            ]);

        // 缺失条件的 Match 目标必须在发布时被拒绝。
        using var missingCondition = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/bindings",
            token,
            new
            {
                bindingKey = $"mc-{Guid.NewGuid():N}"[..16],
                dispatchModeKey = "match",
                producerKey = producer,
                sceneKey = "order.invalid",
                channelKey = TestNotificationProvider.ChannelKey,
                targets = new[] { new { profileKey = primary.ProfileKey, order = 1 } },
            });
        using var missingConditionResponse = await client.SendAsync(missingCondition, cancellationToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingConditionResponse.StatusCode);
        NotificationProfileBindingAssertions.AssertProblem(
            await missingConditionResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.BindingValidationFailed);

        await CreateAndPublishBindingAsync(
            client,
            token,
            $"mb-{Guid.NewGuid():N}"[..16],
            producer,
            scene,
            "match",
            TestNotificationProvider.ChannelKey,
            [
                new
                {
                    profileKey = primary.ProfileKey,
                    order = 1,
                    condition = new { parameterName = "channel", operatorKey = "equals", expectedValue = "sms" },
                },
                new
                {
                    profileKey = secondary.ProfileKey,
                    order = 2,
                    condition = new { parameterName = "channel", operatorKey = "equals", expectedValue = "im" },
                },
            ],
            cancellationToken);

        // 唯一命中：只生成命中目标的投递。
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(
            client,
            token,
            producer,
            scene,
            TestNotificationProvider.ChannelKey,
            cancellationToken);
        var hit = await CreateIntentAsync(
            client,
            token,
            producer,
            scene,
            routedTemplateKey,
            recipientUserId,
            new { orderNo = "M-1", channel = "im" },
            cancellationToken);
        var hitDeliveries = await ListDeliveriesForIntentAsync(client, token, hit.Id, cancellationToken);
        Assert.AreEqual(1, hitDeliveries.Count, "Match 必须唯一命中。");
        Assert.AreEqual(secondary.LatestPublishedVersionId, hitDeliveries[0].ProviderProfileVersionId);

        // 无命中：失败关闭，不产生投递。
        using var noHit = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            token,
            new
            {
                producerKey = producer,
                sceneKey = scene,
                templateKey = routedTemplateKey,
                recipients = new[] { new { recipientTypeKey = "user", recipientKey = recipientUserId.ToString("N") } },
                parameters = new { orderNo = "M-2", channel = "push" },
                idempotencyKey = $"idem-{Guid.NewGuid():N}",
            });
        using var noHitResponse = await client.SendAsync(noHit, cancellationToken);
        // 路由失败属于业务规则错误，按公开契约映射为 422。
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, noHitResponse.StatusCode);
        NotificationProfileBindingAssertions.AssertProblem(
            await noHitResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.RouteMatchNone);

        // 多命中：失败关闭，不产生歧义投递。
        var ambiguousProducer = $"tests.match.{Guid.NewGuid():N}"[..28];
        await CreateAndPublishBindingAsync(
            client,
            token,
            $"ab-{Guid.NewGuid():N}"[..16],
            ambiguousProducer,
            scene,
            "match",
            TestNotificationProvider.ChannelKey,
            [
                new
                {
                    profileKey = primary.ProfileKey,
                    order = 1,
                    condition = new { parameterName = "channel", operatorKey = "not_equals", expectedValue = "none" },
                },
                new
                {
                    profileKey = secondary.ProfileKey,
                    order = 2,
                    condition = new { parameterName = "channel", operatorKey = "exists" },
                },
            ],
            cancellationToken);
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(
            client,
            token,
            ambiguousProducer,
            scene,
            TestNotificationProvider.ChannelKey,
            cancellationToken);
        using var ambiguous = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            token,
            new
            {
                producerKey = ambiguousProducer,
                sceneKey = scene,
                templateKey = routedTemplateKey,
                recipients = new[] { new { recipientTypeKey = "user", recipientKey = recipientUserId.ToString("N") } },
                parameters = new { orderNo = "M-3", channel = "sms" },
                idempotencyKey = $"idem-{Guid.NewGuid():N}",
            });
        using var ambiguousResponse = await client.SendAsync(ambiguous, cancellationToken);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, ambiguousResponse.StatusCode);
        NotificationProfileBindingAssertions.AssertProblem(
            await ambiguousResponse.Content.ReadAsStringAsync(cancellationToken),
            NotificationsErrorCodes.RouteMatchAmbiguous);
    }

    /// <summary>
    /// 提前回执：回执先于 Attempt 到达时暂存为待关联，Attempt 提交后由有界补偿推进为已完成并落地 delivered。
    /// </summary>
    private static async Task VerifyEarlyReceiptReconciliationAsync(
        FullNetApiFactory factory,
        HttpClient client,
        string token,
        Guid recipientUserId,
        NotificationProviderProfileResponse profile,
        CancellationToken cancellationToken)
    {
        var producer = $"tests.early.{Guid.NewGuid():N}"[..28];
        const string scene = "order.early";
        await CreateAndPublishBindingAsync(
            client,
            token,
            $"eb-{Guid.NewGuid():N}"[..16],
            producer,
            scene,
            "single",
            TestNotificationProvider.ChannelKey,
            [new { profileKey = profile.ProfileKey, order = 1 }],
            cancellationToken);
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(
            client,
            token,
            producer,
            scene,
            TestNotificationProvider.ChannelKey,
            cancellationToken);
        var templateKey = await CreateTemplateAsync(
            client,
            token,
            $"et-{Guid.NewGuid():N}"[..16],
            cancellationToken);

        var harness = factory.Services.GetRequiredService<TestNotificationProviderHarness>();
        harness.Reset(TestNotificationProviderMode.Succeed);
        var intent = await CreateIntentAsync(
            client, token, producer, scene, templateKey, recipientUserId, new { orderNo = "E-1" }, cancellationToken);
        var delivery = (await ListDeliveriesForIntentAsync(client, token, intent.Id, cancellationToken)).Single();
        Assert.AreEqual("accepted", delivery.StatusKey);

        // 预取提供程序消息号：测试适配器返回 test-msg-{幂等键}，幂等键由 Intent、收件人与 ProfileVersion 组成。
        var recipientId = intent.Recipients.Single().Id;
        var providerMessageId = $"test-msg-{intent.Id:N}:{recipientId:N}:{delivery.ProviderProfileVersionId!.Value:N}";
        var receiptBody = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                receiptIdempotencyKey = $"early-{delivery.Id:N}",
                providerMessageId,
                externalStatusKey = "delivered",
                mappedStatusKey = "delivered",
            }));
        using var early = await SendReceiptAsync(
            client,
            TestNotificationProvider.ProviderTypeKeyValue,
            receiptBody,
            TestNotificationReceiptVerifier.Sign(receiptBody),
            cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, early.StatusCode);
        var earlyAccepted = await early.Content.ReadFromJsonAsync<NotificationReceiptAcceptedResponse>(cancellationToken);
        Assert.IsNotNull(earlyAccepted);
        Assert.AreEqual("pending_association", earlyAccepted.ProcessStatusKey, "回执先于 Attempt 到达时必须暂存为待关联。");

        // Attempt 提交后触发有界补偿：待关联回执被重新匹配并推进到已完成，投递落地 delivered。
        await DrainPendingAsync(factory, cancellationToken);
        var reconciled = await GetDeliveryAsync(client, token, delivery.Id, cancellationToken);
        Assert.AreEqual("delivered", reconciled.StatusKey, "提前回执在 Attempt 提交后必须被补偿推进为已送达。");
        Assert.AreEqual(1, reconciled.Attempts.Count);
        var receipt = reconciled.Receipts.Single();
        Assert.AreEqual("completed", receipt.ProcessStatusKey);
        Assert.IsNotNull(receipt.ProcessedAtUtc);

        // 模拟 CAS 冲突或 Attempt 提交后中断：不再让厂商重发，空闲 Worker 也必须收敛。
        foreach (var pendingState in new[] { "pending_apply", "pending_association" })
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            await commands.ExecuteAsync(new SqlStatement("tests.notifications.recovery_delivery", """
                UPDATE fn_notifications_delivery SET StatusKey = 'sent', Revision = Revision + 1 WHERE Id = @Id
                """, SqlDataScope.Global), NotificationPlatformSqlParameters.Create(("Id", delivery.Id)), cancellationToken);
            await commands.ExecuteAsync(new SqlStatement("tests.notifications.recovery_receipt", """
                UPDATE fn_notifications_receipt SET ProcessStatusKey = @StateKey, ProcessedAtUtc = NULL, DeliveryId = NULL WHERE Id = @Id
                """, SqlDataScope.Global), NotificationPlatformSqlParameters.Create(("Id", receipt.Id), ("StateKey", pendingState)), cancellationToken);
            await scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>().SampleBacklogAsync(cancellationToken);
            var recovered = await GetDeliveryAsync(client, token, delivery.Id, cancellationToken);
            Assert.AreEqual("delivered", recovered.StatusKey, "空闲 Worker 应恢复待关联及待应用回执。");
            Assert.AreEqual("completed", recovered.Receipts.Single().ProcessStatusKey);
            Assert.AreEqual(1, recovered.Attempts.Count, "回执补偿不能重新外发。");
        }
    }

    private static async Task<NotificationProviderProfileResponse> CreateEnabledProfileAsync(
        HttpClient client,
        string token,
        string profileKey,
        CancellationToken cancellationToken)
    {
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(
            client, token, profileKey, cancellationToken);
        return await NotificationProfileBindingAssertions.PublishAndEnableAsync(
            client, token, profile, cancellationToken);
    }

    private static async Task<string> CreateTemplateAsync(
        HttpClient client,
        string token,
        string templateKey,
        CancellationToken cancellationToken,
        object[]? parameters = null)
    {
        using var create = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/templates",
            token,
            new
            {
                templateKey,
                channelKey = TestNotificationProvider.ChannelKey,
                contentCategoryKey = "transactional",
                draftSubject = "订单 {orderNo}",
                draftBody = new { text = "正文 {orderNo}" },
                parameterSchema = new
                {
                    schemaVersion = 1,
                    parameters = parameters ?? [
                        new { name = "orderNo", typeKey = "string", required = true, maxLength = 32 },
                    ],
                },
            });
        using var createResponse = await client.SendAsync(create, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            createResponse.StatusCode,
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var created = await createResponse.Content.ReadFromJsonAsync<NotificationTemplateResponse>(cancellationToken);
        Assert.IsNotNull(created);
        using var publish = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/templates/{created.Id:D}/publish",
            token,
            new PublishNotificationTemplateRequest(created.Version, "c1"));
        using var publishResponse = await client.SendAsync(publish, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, publishResponse.StatusCode);
        return templateKey;
    }

    private static async Task<NotificationBindingResponse> CreateAndPublishBindingAsync(
        HttpClient client,
        string token,
        string bindingKey,
        string producer,
        string scene,
        string dispatchModeKey,
        string channelKey,
        object[] targets,
        CancellationToken cancellationToken)
    {
        using var create = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/bindings",
            token,
            new { bindingKey, dispatchModeKey, producerKey = producer, sceneKey = scene, channelKey, targets });
        using var createResponse = await client.SendAsync(create, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.Created,
            createResponse.StatusCode,
            await createResponse.Content.ReadAsStringAsync(cancellationToken));
        var created = await createResponse.Content.ReadFromJsonAsync<NotificationBindingResponse>(cancellationToken);
        Assert.IsNotNull(created);
        using var publish = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            $"/api/v1/notifications/bindings/{created.Id:D}/publish",
            token,
            new PublishNotificationBindingRequest(created.Version));
        using var publishResponse = await client.SendAsync(publish, cancellationToken);
        Assert.AreEqual(
            HttpStatusCode.OK,
            publishResponse.StatusCode,
            await publishResponse.Content.ReadAsStringAsync(cancellationToken));
        var published = await publishResponse.Content.ReadFromJsonAsync<NotificationBindingResponse>(cancellationToken);
        Assert.IsNotNull(published);
        return published;
    }

    private static async Task<NotificationIntentResponse> CreateIntentAsync(
        HttpClient client,
        string token,
        string producer,
        string scene,
        string templateKey,
        Guid recipientUserId,
        object parameters,
        CancellationToken cancellationToken)
    {
        using var request = NotificationProfileBindingAssertions.CreateBearerJsonRequest(
            HttpMethod.Post,
            "/api/v1/notifications/intents",
            token,
            new
            {
                producerKey = producer,
                sceneKey = scene,
                templateKey,
                recipients = new[] { new { recipientTypeKey = "user", recipientKey = recipientUserId.ToString("N") } },
                parameters,
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

    private static async Task<IReadOnlyList<NotificationDeliveryResponse>> ListDeliveriesForIntentAsync(
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
        var results = new List<NotificationDeliveryResponse>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("intentId").GetGuid() != intentId)
            {
                continue;
            }

            results.Add(await GetDeliveryAsync(client, token, item.GetProperty("id").GetGuid(), cancellationToken));
        }

        return results;
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
        var delivery = await response.Content.ReadFromJsonAsync<NotificationDeliveryResponse>(cancellationToken);
        Assert.IsNotNull(delivery);
        return delivery;
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

    /// <summary>执行单轮领取与处理，用于模拟两个 Worker 同时竞争同一批投递。</summary>
    private static async Task<int> ProcessPendingOnceAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>();
        return await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>把当前可执行投递全部处理完，避免遗留 accepted 投递影响后续场景断言。</summary>
    private static async Task<List<int>> DrainPendingAsync(FullNetApiFactory factory, CancellationToken cancellationToken)
    {
        var batches = new List<int>();
        for (var i = 0; i < 16; i++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>();
            var processed = await processor.ProcessPendingAsync(cancellationToken).ConfigureAwait(false);
            batches.Add(processed);
            if (processed == 0)
            {
                break;
            }
        }

        return batches;
    }
}
