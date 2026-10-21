using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Features.ManageRefunds;
using Full.NET.Modules.Payments.Persistence;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Document.PreviewTasks;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Document;

/// <summary>真实双库验证文档授权、并发领取、过期恢复及旧执行者隔离。</summary>
internal static class ProjectReviewRemediationAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory)
    {
        await factory.InitializeAsync();
        using var client = factory.CreateClientForHost("localhost");
        string[] permissions = [HostDocumentPermissions.Read, HostDocumentPermissions.Create, HostDocumentPermissions.Update,
            HostDocumentPermissions.Download, HostDocumentPermissionManagementPermissions.Set, HostDocumentSharePermissions.Create];
        var alice = await factory.CreateHostIdentityAsync($"acl-a-{Guid.NewGuid():N}", permissions);
        var bob = await factory.CreateHostIdentityAsync($"acl-b-{Guid.NewGuid():N}", permissions);
        using var createdResponse = await SendAsync(client, alice.AccessToken, HttpMethod.Post, "/api/v1/document/host/items",
            new CreateHostDocumentItemRequest("Restricted", "ACL regression"));
        Assert.AreEqual(HttpStatusCode.Created, createdResponse.StatusCode);
        var document = (await createdResponse.Content.ReadFromJsonAsync<HostDocumentItemResponse>())!;
        using var setResponse = await SendAsync(client, alice.AccessToken, HttpMethod.Post, "/api/v1/document/host/permissions",
            new SetHostDocumentPermissionsRequest(document.Id, [new(alice.UserId, "read")]));
        Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);
        foreach (var suffix in new[] { "", "/content", "/preview", "/versions" })
        {
            using var denied = await SendAsync(client, bob.AccessToken, HttpMethod.Get, $"/api/v1/document/host/items/{document.Id}{suffix}");
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode, suffix);
        }
        using (var allowed = await SendAsync(client, alice.AccessToken, HttpMethod.Get, $"/api/v1/document/host/items/{document.Id}"))
            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        using (var list = await SendAsync(client, bob.AccessToken, HttpMethod.Get, "/api/v1/document/host/items"))
        {
            var page = (await list.Content.ReadFromJsonAsync<PagedResult<HostDocumentItemResponse>>())!;
            Assert.IsFalse(page.Items.Any(item => item.Id == document.Id));
            Assert.AreEqual(0L, page.Total);
        }
        using (var deniedShare = await SendAsync(client, alice.AccessToken, HttpMethod.Post, "/api/v1/document/host/shares",
            new CreateHostDocumentShareRequest(document.Id, 1, null, null)))
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedShare.StatusCode);
        using (var invalidLevel = await SendAsync(client, alice.AccessToken, HttpMethod.Post, "/api/v1/document/host/permissions",
            new SetHostDocumentPermissionsRequest(document.Id, [new(alice.UserId, "anything")])))
            Assert.AreEqual(HttpStatusCode.BadRequest, invalidLevel.StatusCode);

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        firstScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        secondScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var commands = firstScope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var now = DateTimeOffset.UtcNow;
        var taskId = Guid.NewGuid();
        await commands.ExecuteAsync(DocumentPreviewTaskSql.Insert, Parameters(
            ("Id", taskId), ("DocumentItemId", document.Id), ("VersionId", null), ("DocumentTitle", "Restricted"),
            ("SourceFileId", Guid.NewGuid()), ("SourceFileName", "test.docx"), ("SourceMimeType", "application/octet-stream"),
            ("OutputFileId", null), ("StatusKey", "pending"), ("ProviderKey", "disabled"), ("ErrorCode", null),
            ("RequestedByUserId", alice.UserId), ("CreatedAtUtc", now), ("StartedAtUtc", null), ("CompletedAtUtc", null), ("Version", 1L)), default);
        var runners = new[] { firstScope, secondScope }.Select(scope => ActivatorUtilities.CreateInstance<DocumentPreviewTaskRunner>(scope.ServiceProvider)).ToArray();
        var claims = await Task.WhenAll(runners.Select(runner => runner.ClaimAsync(1, default)));
        Assert.AreEqual(1, claims.Sum(batch => batch.Count));
        var oldTask = claims.SelectMany(batch => batch).Single();
        await commands.ExecuteAsync(new SqlStatement("test.expire_preview", "UPDATE fn_document_preview_task SET StartedAtUtc = @Old WHERE Id = @Id", SqlDataScope.HostOnly),
            Parameters(("Old", now.AddHours(-2)), ("Id", taskId)), default);
        var recovered = (await runners[0].ClaimAsync(1, default)).Single();
        Assert.IsTrue(recovered.Version > oldTask.Version);
        Assert.AreEqual(0, await commands.ExecuteAsync(DocumentPreviewTaskSql.MarkSucceeded,
            Parameters(("Id", taskId), ("OutputFileId", Guid.NewGuid()), ("Version", oldTask.Version), ("ExpiredBefore", now.AddHours(-1)), ("CompletedAtUtc", now)), default));
        Assert.AreEqual(1, await commands.ExecuteAsync(DocumentPreviewTaskSql.MarkSucceeded,
            Parameters(("Id", taskId), ("OutputFileId", Guid.NewGuid()), ("Version", recovered.Version), ("ExpiredBefore", now.AddHours(-1)), ("CompletedAtUtc", now)), default));

        var merchantId = Guid.NewGuid(); var orderId = Guid.NewGuid(); var refundId = Guid.NewGuid();
        await commands.ExecuteAsync(PaymentMerchantConfigSql.Insert, Parameters(("Id", merchantId), ("TenantId", null),
            ("Name", "test"), ("ChannelKey", PaymentChannelKeys.WeChatNative), ("AppId", "app"), ("MerchantId", "merchant"),
            ("CertificateSerialNo", "test-serial"), ("NotifyUrl", "https://example.com/notify"), ("ReturnUrl", null),
            ("ApiV3KeyProtected", "test-protected"), ("PrivateKeyProtected", "test-protected"), ("IsDefault", false), ("IsEnabled", true),
            ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("Version", 1)), default);
        await commands.ExecuteAsync(PaymentOrderSql.Insert, Parameters(("Id", orderId), ("TenantId", Guid.NewGuid()),
            ("MerchantConfigId", merchantId), ("ChannelKey", PaymentChannelKeys.WeChatNative), ("OutTradeNo", orderId.ToString("N")),
            ("TradeStateKey", PaymentTradeStateKeys.Refunding), ("AmountMinor", 500L), ("Currency", "CNY"),
            ("Subject", "test"), ("Description", null), ("CodeUrl", null), ("ProviderTransactionId", "wx-pay"), ("FailMessage", null),
            ("CreatedAtUtc", now), ("UpdatedAtUtc", now), ("PaidAtUtc", now), ("Version", 1)), default);
        var queryExecutor = firstScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var order = (await queryExecutor.QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindById,
            Parameters(("OrderId", orderId)), default))!;
        await commands.ExecuteAsync(PaymentRefundSql.Insert, Parameters(("Id", refundId), ("TenantId", order.TenantId),
            ("OrderId", orderId), ("MerchantConfigId", merchantId), ("OutTradeNo", order.OutTradeNo), ("OutRefundNo", "refund-test"),
            ("RefundStateKey", PaymentRefundStateKeys.Processing), ("AmountMinor", 500L), ("Currency", "CNY"), ("Reason", "test"),
            ("ProviderRefundId", "wx-refund"), ("FailMessage", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now),
            ("CompletedAtUtc", null), ("Version", 1)), default);
        var refundReconciliation = new PaymentRefundReconciliationService(queryExecutor, commands,
            firstScope.ServiceProvider.GetRequiredService<ICommandTransaction>(), new RefundClient(),
            firstScope.ServiceProvider.GetRequiredService<IClock>());
        Assert.IsTrue((await refundReconciliation.ReconcileAsync(order, default)).IsSuccess);
        var finalOrder = (await queryExecutor.QuerySingleOrDefaultAsync<PaymentOrderRecord>(PaymentOrderSql.FindById,
            Parameters(("OrderId", orderId)), default))!;
        var finalRefund = (await queryExecutor.QuerySingleOrDefaultAsync<PaymentRefundRecord>(PaymentRefundSql.FindById,
            Parameters(("RefundId", refundId)), default))!;
        Assert.AreEqual(PaymentTradeStateKeys.Refunded, finalOrder.TradeStateKey);
        Assert.AreEqual(PaymentRefundStateKeys.Succeeded, finalRefund.RefundStateKey);

        var subscriptionId = Guid.NewGuid(); var deliveryId = Guid.NewGuid();
        await commands.ExecuteAsync(new SqlStatement("test.insert_webhook_subscription", """
            INSERT INTO fn_webhooks_subscription (Id,TenantId,EventType,TargetUrl,SigningSecretHash,IsActive,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@TenantId,'event','https://93.184.216.34/hook','unused',1,@Now,@Now,1)
            """, SqlDataScope.HostOnly), Parameters(("Id", subscriptionId), ("TenantId", Guid.NewGuid()), ("Now", now)), default);
        await commands.ExecuteAsync(WebhookDeliverySql.InsertDelivery, Parameters(("Id", deliveryId), ("SubscriptionId", subscriptionId),
            ("EventId", Guid.NewGuid()), ("PayloadBody", "{}"), ("PayloadDigest", "digest"), ("Status", "pending"),
            ("NextAttemptAtUtc", null), ("CreatedAtUtc", now), ("UpdatedAtUtc", now)), default);
        var processors = new[] { firstScope, secondScope }.Select(scope => scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>()).ToArray();
        var webhookClaims = await Task.WhenAll(processors.Select(processor => processor.ClaimAsync(default)));
        Assert.AreEqual(1, webhookClaims.Sum(batch => batch.Count));
        var oldWebhook = webhookClaims.SelectMany(batch => batch).Single();
        await commands.ExecuteAsync(new SqlStatement("test.expire_webhook", "UPDATE fn_webhooks_delivery SET UpdatedAtUtc = @Old WHERE Id = @Id", SqlDataScope.HostOnly),
            Parameters(("Old", now.AddHours(-2)), ("Id", deliveryId)), default);
        var resumed = (await processors[0].ClaimAsync(default)).Single();
        Assert.IsTrue(resumed.Version > oldWebhook.Version);
        Assert.AreEqual(0, await commands.ExecuteAsync(WebhookDeliverySql.MarkDelivered, Parameters(("DeliveryId", deliveryId),
            ("Status", "delivered"), ("UpdatedAtUtc", now), ("Version", oldWebhook.Version)), default));
        Assert.AreEqual(1, await commands.ExecuteAsync(WebhookDeliverySql.MarkDelivered, Parameters(("DeliveryId", deliveryId),
            ("Status", "delivered"), ("UpdatedAtUtc", now), ("Version", resumed.Version)), default));
    }

    private sealed class RefundClient : IWeChatNativePayClient
    {
        public Task<WeChatRefundQueryResult> QueryDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig,
            string outRefundNo, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WeChatRefundQueryResult(true, "wx-refund", outRefundNo, "SUCCESS", 500, "CNY", null));
        public Task<WeChatNativePayResult> CreateNativeOrderAsync(PaymentMerchantConfigRecord merchantConfig,
            string outTradeNo, long amountMinor, string currency, string description, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatTransactionQueryResult> QueryTransactionByOutTradeNoAsync(PaymentMerchantConfigRecord merchantConfig,
            string outTradeNo, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WeChatRefundResult> CreateDomesticRefundAsync(PaymentMerchantConfigRecord merchantConfig, string outTradeNo,
            string outRefundNo, long amountMinor, long totalMinor, string currency, string reason, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static IReadOnlyDictionary<string, object?> Parameters(params (string Key, object? Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value);

    private static Task<HttpResponseMessage> SendAsync<T>(HttpClient client, string token, HttpMethod method, string path, T body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method, string path) =>
        SendAsync<object?>(client, token, method, path, null);
}
