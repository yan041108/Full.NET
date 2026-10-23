using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Dapper;
using DbUp;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Features.ManageWeChatMiniProgramBindings;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>复用双库通知宿主，验证个人归档、业务撤回、精确权限和本地一次性授权竞争。</summary>
internal static class NotificationLifecycleAssertions
{
    /// <summary>在独立临时库模拟部分 DDL 已提交、脚本未记账；恢复只选固定 284/285，保留已写数据。</summary>
    internal static async Task VerifyRecoveryAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = Options.Create(new DatabaseOptions { Provider = provider, ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16, CommandTimeoutSeconds = 300 });
        await new DbUpMigrationRunner(options, NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions()).MigrateAsync();
        await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        await connection.OpenAsync();
        var id = Guid.CreateVersion7();
        var templateId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var intentId = Guid.CreateVersion7();
        await connection.ExecuteAsync(NotificationPlatformSql.InsertTemplateHost.Text, new
        {
            Id = templateId, TemplateKey = "recovery-snapshot", LocaleTag = "zh-CN", DefaultLocaleTag = "zh-CN",
            ChannelKey = "inbox", ContentCategoryKey = "transactional", DraftSubject = "保留",
            DraftBodyJson = "{}", DraftParameterSchemaJson = "{}", CreatedById = actorId, CreatedAtUtc = DateTime.UtcNow,
        });
        await connection.ExecuteAsync(NotificationPlatformSql.InsertTemplateVersion.Text, new
        {
            Id = versionId, TemplateId = templateId, LocaleTag = "zh-CN", VersionNumber = 1, SchemaVersion = 1,
            Subject = "保留", BodyJson = "{}", ParameterSchemaJson = "{}", ContentClassificationKey = "public",
            ContentHash = new string('b', 64), PublishedById = actorId, PublishedAtUtc = DateTime.UtcNow,
        });
        await connection.ExecuteAsync(NotificationPlatformSql.InsertIntentHost.Text, new
        {
            Id = intentId, ProducerKey = "recovery", SceneKey = "snapshot", IdempotencyKey = "preserve",
            TemplateVersionId = versionId, BindingVersionId = (Guid?)null, PolicyCategoryKey = "transactional",
            DispatchModeKey = "single", RouteSnapshotJson = """[{"profileKey":"preserve","order":1}]""",
            ParameterSnapshotJson = "{}", StatusKey = "accepted", CreatedById = actorId, CreatedAtUtc = DateTime.UtcNow,
        });
        await connection.ExecuteAsync("""
            INSERT INTO fn_notifications_subscription_grant (Id, BindingId, TemplateId, AuthorizationId, ReportedStatusKey, StateKey, CreatedAtUtc)
            VALUES (@Id, @BindingId, 'preserve', @AuthorizationId, 'accepted', 'pending_reconciliation', @Now)
            """, new { Id = id, BindingId = Guid.CreateVersion7(), AuthorizationId = Guid.CreateVersion7(), Now = DateTime.UtcNow });
        // 仅删除隔离测试库中新加的空列；其他生命周期列保留，形成建列步骤半完成状态。
        await connection.ExecuteAsync("ALTER TABLE fn_notifications_delivery DROP COLUMN DispatchLeaseGeneration");
        if (provider == DatabaseProvider.SqlServer)
            await connection.ExecuteAsync("DROP INDEX IX_fn_notifications_subscription_grant_CreatedAtUtc ON fn_notifications_subscription_grant");
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%284_NotificationsSubscriptionGrant.sql' OR ScriptName LIKE '%285_NotificationsMessageLifecycle.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%284_NotificationsSubscriptionGrant.sql' OR ScriptName LIKE '%285_NotificationsMessageLifecycle.sql'");
        Assert.AreEqual(2, Recover());
        Assert.AreEqual(0, Recover());
        await connection.ExecuteAsync(provider == DatabaseProvider.SqlServer
            ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%285_NotificationsMessageLifecycle.sql'"
            : "DELETE FROM schemaversions WHERE ScriptName LIKE '%285_NotificationsMessageLifecycle.sql'");
        Assert.AreEqual(1, Recover());
        using (var route = JsonDocument.Parse(await connection.QuerySingleAsync<string>(
            "SELECT RouteSnapshotJson FROM fn_notifications_intent WHERE Id = @Id", new { Id = intentId })))
        {
            Assert.AreEqual(1, route.RootElement.GetProperty("targets").GetArrayLength());
            Assert.AreEqual("preserve", route.RootElement.GetProperty("targets")[0].GetProperty("profileKey").GetString());
        }
        Assert.AreEqual("pending_reconciliation", await connection.QuerySingleAsync<string>(
            "SELECT StateKey FROM fn_notifications_subscription_grant WHERE Id = @Id", new { Id = id }));
        Assert.AreEqual(1, await connection.QuerySingleAsync<int>(provider == DatabaseProvider.SqlServer
            ? "SELECT COUNT(1) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.fn_notifications_delivery') AND name = 'DispatchLeaseGeneration'"
            : "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_delivery' AND COLUMN_NAME = 'DispatchLeaseGeneration'"));
        int Recover()
        {
            var builder = provider == DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(connectionString)
                : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            var segment = provider == DatabaseProvider.SqlServer ? ".SqlServer." : ".MySql.";
            var result = builder.WithScriptsEmbeddedInAssembly(MigrationAssembly.Value, name => name.Contains(segment, StringComparison.Ordinal)
                    && (name.EndsWith(".284_NotificationsSubscriptionGrant.sql", StringComparison.Ordinal)
                        || name.EndsWith(".285_NotificationsMessageLifecycle.sql", StringComparison.Ordinal)))
                .WithExecutionTimeout(TimeSpan.FromSeconds(300)).LogToNowhere().Build().PerformUpgrade();
            Assert.IsTrue(result.Successful, result.Error?.ToString());
            return result.Scripts.Count();
        }
    }

    internal static async Task VerifySendBoundaryAsync(FullNetApiFactory factory, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, cancellationToken);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, cancellationToken);
        var route = await NotificationDeliveryWorkerAssertions.CreateRouteAsync(client, token, user.Id, cancellationToken);
        var harness = factory.Services.GetRequiredService<TestNotificationProviderHarness>();
        harness.Reset();
        var before = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, token, route, user.Id, "revoke-before", cancellationToken);
        var beforeDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, before.Id, cancellationToken);
        // 模拟已预留但尚未进入外发边界的 Worker 中断；撤回必须自己收敛本轮额度。
        await using (var reservedScope = factory.Services.CreateAsyncScope())
        {
            reservedScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await reservedScope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(
                new SqlStatement("tests.notifications.claim_before_revoke", """
                    UPDATE fn_notifications_delivery SET LeaseGeneration = 1, LeaseOwnerKey = 'crashed-before-boundary',
                        LeaseExpiresAtUtc = @Until WHERE Id = @Id
                    """, SqlDataScope.Global), NotificationPlatformSqlParameters.Create(("Id", beforeDelivery.Id),
                    ("Until", DateTimeOffset.UtcNow.AddMinutes(5))), cancellationToken);
            var reservation = await reservedScope.ServiceProvider.GetRequiredService<NotificationQuotaGovernor>().ReserveAsync(
                beforeDelivery.Id, 1, "host", TestNotificationProvider.ProviderTypeKeyValue, route.Producer, route.Scene, cancellationToken);
            Assert.AreEqual(NotificationQuotaReservationStatus.Reserved, reservation.Status);
        }
        using (var revoke = await SendAsync(client, token, HttpMethod.Post, $"/api/v1/notifications/intents/{before.Id:D}/revoke",
            new RevokeNotificationIntentRequest(before.Revision, "cancel-before-send"), cancellationToken))
        {
            Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);
            Assert.AreEqual(1, (await revoke.Content.ReadFromJsonAsync<NotificationRevocationResponse>(cancellationToken))!.StoppedDeliveries);
        }
        await using (var releasedScope = factory.Services.CreateAsyncScope())
        {
            releasedScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var queries = releasedScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            Assert.AreEqual(0, (await queries.QueryAsync<NotificationQuotaReservationRecord>(NotificationLifecycleSql.PendingQuotas,
                NotificationPlatformSqlParameters.Create(("DeliveryId", beforeDelivery.Id)), cancellationToken)).Count);
            var released = await queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("tests.notifications.revocation_release", """
                SELECT COUNT(1) FROM fn_notifications_quota_reservation WHERE DeliveryId = @Id AND StateKey = 'released'
                """, SqlDataScope.Global), NotificationPlatformSqlParameters.Create(("Id", beforeDelivery.Id)), cancellationToken);
            Assert.AreEqual(4L, released, "撤回自身必须释放未使用的四维额度，不依赖 Worker 再次运行。");
        }
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        Assert.AreEqual(0, harness.IdempotencyKeysFor(beforeDelivery.Id).Count);

        var during = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, token, route, user.Id, "revoke-during", cancellationToken);
        var duringDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, during.Id, cancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.OnSendAsync = async (request, token) =>
        {
            if (request.DeliveryId != duringDelivery.Id) return;
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
        };
        var worker = NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            using var revoke = await SendAsync(client, token, HttpMethod.Post, $"/api/v1/notifications/intents/{during.Id:D}/revoke",
                new RevokeNotificationIntentRequest(during.Revision, "cancel-after-boundary"), cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);
            var outcome = (await revoke.Content.ReadFromJsonAsync<NotificationRevocationResponse>(cancellationToken))!;
            Assert.AreEqual(1, outcome.CrossedSendBoundary);
            Assert.AreEqual(0, outcome.StoppedDeliveries);
        }
        finally { release.TrySetResult(); await worker; harness.OnSendAsync = null; }
        Assert.AreEqual("sent", (await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(client, token, duringDelivery.Id, cancellationToken)).StatusKey);

        var expiry = DateTimeOffset.UtcNow.AddSeconds(2);
        using var createExpired = await SendAsync(client, token, HttpMethod.Post, "/api/v1/notifications/intents",
            new CreateNotificationIntentRequest(route.Producer, route.Scene, route.TemplateKey, [new("user", user.Id.ToString("N"))],
                JsonSerializer.SerializeToElement(new { orderNo = "expires" }), $"expiry-{Guid.NewGuid():N}", ExpiresAtUtc: expiry), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, createExpired.StatusCode);
        var expiring = (await createExpired.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken))!;
        var expiringDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, expiring.Id, cancellationToken);
        var wait = expiry - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait + TimeSpan.FromMilliseconds(50), cancellationToken);
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        expiringDelivery = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(client, token, expiringDelivery.Id, cancellationToken);
        Assert.AreEqual("suppressed", expiringDelivery.StatusKey);
        Assert.AreEqual("notifications.message_expired", expiringDelivery.SuppressionReasonCode);
        Assert.AreEqual(0, harness.IdempotencyKeysFor(expiringDelivery.Id).Count);
        using var summary = await SendAsync(client, token, HttpMethod.Get, "/api/v1/notifications/deliveries/summary", null, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, summary.StatusCode);
        var rows = (await summary.Content.ReadFromJsonAsync<NotificationDeliverySummaryResponse[]>(cancellationToken))!;
        Assert.IsTrue(rows.Any(row => row.StatusKey == "suppressed" && row.Count > 0));
        Assert.IsTrue(rows.Any(row => row.ProviderTypeKey == TestNotificationProvider.ProviderTypeKeyValue && row.ReceiptModeKey == "signed"));
        harness.Reset();
        harness.Mode = TestNotificationProviderMode.Unknown;
        var uncertain = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, token, route, user.Id, "manual-reconcile", cancellationToken);
        var uncertainDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, uncertain.Id, cancellationToken);
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        uncertainDelivery = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(client, token, uncertainDelivery.Id, cancellationToken);
        Assert.AreEqual("unknown", uncertainDelivery.StatusKey);
        var readOnly = await factory.CreateHostAccessTokenAsync([NotificationPlatformPermissions.DeliveriesRead], cancellationToken);
        var reconcilePath = $"/api/v1/notifications/deliveries/{uncertainDelivery.Id:D}/reconcile";
        var disposition = new ReconcileNotificationDeliveryRequest(uncertainDelivery.Revision, "confirmed_not_called", "provider-case-20261023");
        using var denied = await SendAsync(client, readOnly, HttpMethod.Post, reconcilePath, disposition, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
        using var reconciled = await SendAsync(client, token, HttpMethod.Post, reconcilePath, disposition, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, reconciled.StatusCode, await reconciled.Content.ReadAsStringAsync(cancellationToken));
        Assert.AreEqual("failed", (await reconciled.Content.ReadFromJsonAsync<NotificationDeliveryResponse>(cancellationToken))!.StatusKey);
        using var replayDisposition = await SendAsync(client, token, HttpMethod.Post, reconcilePath, disposition, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, replayDisposition.StatusCode);
        await AssertDispositionAsync(uncertainDelivery.Id, "released");
        var acceptedUnknown = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, token, route, user.Id, "manual-called", cancellationToken);
        var acceptedUnknownDelivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, acceptedUnknown.Id, cancellationToken);
        await NotificationDeliveryWorkerAssertions.ProcessPendingAsync(factory, cancellationToken);
        acceptedUnknownDelivery = await NotificationDeliveryWorkerAssertions.GetDeliveryAsync(client, token, acceptedUnknownDelivery.Id, cancellationToken);
        using var called = await SendAsync(client, token, HttpMethod.Post,
            $"/api/v1/notifications/deliveries/{acceptedUnknownDelivery.Id:D}/reconcile",
            new ReconcileNotificationDeliveryRequest(acceptedUnknownDelivery.Revision, "confirmed_accepted", "provider-case-called"), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, called.StatusCode);
        Assert.AreEqual("sent", (await called.Content.ReadFromJsonAsync<NotificationDeliveryResponse>(cancellationToken))!.StatusKey);
        await AssertDispositionAsync(acceptedUnknownDelivery.Id, "consumed");
        harness.Reset();

        async Task AssertDispositionAsync(Guid deliveryId, string state)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var count = await queries.QuerySingleOrDefaultAsync<long>(new SqlStatement("tests.notifications.resolved_quota", """
                SELECT COUNT(1) FROM fn_notifications_quota_reservation
                WHERE DeliveryId = @Id AND StateKey = @StateKey
                """, SqlDataScope.Global), NotificationPlatformSqlParameters.Create(("Id", deliveryId), ("StateKey", state)), cancellationToken);
            Assert.AreEqual(4L, count, "四个配额维度必须与人工结论同事务收敛。");
            Assert.AreEqual(0, (await queries.QueryAsync<NotificationQuotaReservationRecord>(NotificationLifecycleSql.PendingQuotas,
                NotificationPlatformSqlParameters.Create(("DeliveryId", deliveryId)), cancellationToken)).Count);
        }
    }

    internal static async Task VerifyInboxAsync(FullNetApiFactory factory, HttpClient client, string token,
        Guid userId, string templateKey, CancellationToken cancellationToken)
    {
        var link = new NotificationDeepLink("workflow_todo", Guid.CreateVersion7());
        var body = new CreateNotificationIntentRequest("tests.notifications", "order.paid", templateKey,
            [new("user", userId.ToString("N"))], JsonSerializer.SerializeToElement(new { orderNo = "lifecycle" }),
            $"lifecycle-{Guid.NewGuid():N}", DeepLink: link);
        using var created = await SendAsync(client, token, HttpMethod.Post, "/api/v1/notifications/intents", body, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync(cancellationToken));
        var intent = (await created.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken))!;
        using var list = await SendAsync(client, token, HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages?sceneKey=order.paid&pageSize=100", null, cancellationToken);
        var messages = (await list.Content.ReadFromJsonAsync<PagedResult<InboxMessageResponse>>(cancellationToken))!;
        var message = messages.Items.Single(item => item.DeepLink == link);
        Assert.AreEqual("tests.notifications", message.ProducerKey);
        var readOnly = await factory.CreateHostAccessTokenAsync([InboxPermissions.Read], cancellationToken);
        using var forbidden = await SendAsync(client, readOnly, HttpMethod.Post,
            $"/api/v1/notifications/my-inbox-messages/{message.Id:D}/archive", null, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        for (var repeat = 0; repeat < 2; repeat++)
        {
            using var archive = await SendAsync(client, token, HttpMethod.Post,
                "/api/v1/notifications/my-inbox-messages/archive-batch",
                new ArchiveInboxMessagesRequest([message.Id, Guid.CreateVersion7()]), cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, archive.StatusCode);
        }
        using var archived = await SendAsync(client, token, HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages?archived=true&pageSize=100", null, cancellationToken);
        var archivedMessage = (await archived.Content.ReadFromJsonAsync<PagedResult<InboxMessageResponse>>(cancellationToken))!
            .Items.Single(item => item.Id == message.Id);
        Assert.AreEqual("unread", archivedMessage.Status);
        Assert.IsNotNull(archivedMessage.ArchivedAtUtc);
        using var deniedRevoke = await SendAsync(client, readOnly, HttpMethod.Post,
            $"/api/v1/notifications/intents/{intent.Id:D}/revoke", new RevokeNotificationIntentRequest(intent.Revision, "test"), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, deniedRevoke.StatusCode);
        using var stale = await SendAsync(client, token, HttpMethod.Post,
            $"/api/v1/notifications/intents/{intent.Id:D}/revoke", new RevokeNotificationIntentRequest(intent.Revision + 1, "test"), cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        using var revoke = await SendAsync(client, token, HttpMethod.Post,
            $"/api/v1/notifications/intents/{intent.Id:D}/revoke", new RevokeNotificationIntentRequest(intent.Revision, "business-cancelled"), cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode, await revoke.Content.ReadAsStringAsync(cancellationToken));
        var outcome = (await revoke.Content.ReadFromJsonAsync<NotificationRevocationResponse>(cancellationToken))!;
        Assert.AreEqual(1, outcome.RevokedInboxMessages);
        Assert.AreEqual(0, outcome.CrossedSendBoundary);
        using var after = await SendAsync(client, token, HttpMethod.Get,
            "/api/v1/notifications/my-inbox-messages?archived=true&pageSize=100", null, cancellationToken);
        var withdrawn = (await after.Content.ReadFromJsonAsync<PagedResult<InboxMessageResponse>>(cancellationToken))!
            .Items.Single(item => item.Id == message.Id);
        Assert.IsNotNull(withdrawn.RevokedAtUtc);
        Assert.AreEqual("unread", withdrawn.Status);
        Assert.AreEqual(string.Empty, withdrawn.Content);
        Assert.IsNull(withdrawn.DeepLink);
        using var replay = await SendAsync(client, token, HttpMethod.Post, "/api/v1/notifications/intents", body, cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual("revoked", (await replay.Content.ReadFromJsonAsync<NotificationIntentResponse>(cancellationToken))!.StatusKey);
        using var changedLink = await SendAsync(client, token, HttpMethod.Post, "/api/v1/notifications/intents",
            body with { DeepLink = link with { ResourceId = Guid.CreateVersion7() } }, cancellationToken);
        Assert.AreEqual(HttpStatusCode.Conflict, changedLink.StatusCode);
    }

    internal static async Task VerifySubscriptionAsync(FullNetApiFactory factory, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, cancellationToken);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, cancellationToken);
        var profile = await NotificationProfileBindingAssertions.CreateProfileAsync(client, token, $"grant-{Guid.NewGuid():N}"[..20], cancellationToken);
        profile = await NotificationProfileBindingAssertions.PublishAndEnableAsync(client, token, profile, cancellationToken);
        var profileId = profile.LatestPublishedVersionId!.Value;
        var bindingId = Guid.CreateVersion7();
        var appId = $"wx{Guid.NewGuid():N}"[..24];
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(WeChatMiniProgramBindingSql.Insert,
                NotificationPlatformSqlParameters.Create(("Id", bindingId), ("TenantScopeKey", "host"), ("UserId", user.Id),
                    ("AppId", appId), ("ProviderProfileVersionId", profileId), ("OpenIdProtected", "test-protected"),
                    ("OpenIdMask", "test***"), ("OpenIdSha256Hex", new string('a', 64)), ("UnionIdProtected", null),
                    ("UnionIdMask", null), ("VerificationStatusKey", "verified"), ("RecipientEndpointId", null),
                    ("CreatedAtUtc", DateTimeOffset.UtcNow), ("UpdatedAtUtc", DateTimeOffset.UtcNow)), cancellationToken);
        }
        var reportId = Guid.CreateVersion7();
        await ReportAsync("accepted", reportId);
        await ReportAsync("accepted", reportId);
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var results = await Task.WhenAll(ReserveAsync(first, 1), ReserveAsync(second, 1));
        CollectionAssert.AreEquivalent(new[] { 0, 1 }, results);
        var winner = results[0] == 1 ? first : second;
        await ResolveAsync(winner, "available");
        Assert.AreEqual(1, await ReserveAsync(second, 2));
        await ResolveAsync(second, "consumed", 2);
        await ReportAsync("accepted", reportId);
        Assert.AreEqual(0, await ReserveAsync(Guid.CreateVersion7(), 1), "重复上报不能再生成已核销许可。");
        await ReportAsync("accepted", Guid.CreateVersion7());
        var withdrawnReservation = Guid.CreateVersion7();
        Assert.AreEqual(1, await ReserveAsync(withdrawnReservation, 1));
        await ReportAsync("rejected", Guid.CreateVersion7());
        await ReportAsync("accepted", Guid.CreateVersion7());
        await ResolveAsync(withdrawnReservation, "available");
        var renewed = Guid.CreateVersion7();
        Assert.AreEqual(1, await ReserveAsync(renewed, 1));
        await ResolveAsync(renewed, "consumed");
        Assert.AreEqual(0, await ReserveAsync(Guid.CreateVersion7(), 1), "撤销的旧预留不能随下一次同意复活。");
        await ReportAsync("accepted", Guid.CreateVersion7());
        await ReportAsync("rejected", Guid.CreateVersion7());
        Assert.AreEqual(0, await ReserveAsync(Guid.CreateVersion7(), 1));
        Assert.AreEqual("rejected", await ReportAsync("accepted", reportId), "旧接受重放不能覆盖后来的拒绝。");
        await ReportAsync("accepted", Guid.CreateVersion7());
        var unknown = Guid.CreateVersion7();
        Assert.AreEqual(1, await ReserveAsync(unknown, 1));
        await ResolveAsync(unknown, "pending_reconciliation");
        Assert.AreEqual(-1, await ReserveAsync(unknown, 2), "Unknown 保持暂挂，不自动消耗另一份授权。");
        Assert.AreEqual(0, await ReserveAsync(Guid.CreateVersion7(), 1, "other-scope"));

        await ReportAsync("accepted", Guid.CreateVersion7());
        var oldIdentity = Guid.CreateVersion7();
        Assert.AreEqual(1, await ReserveAsync(oldIdentity, 1));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await sp.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token =>
            {
                await sp.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<Guid>(
                    sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider == DatabaseProvider.MySql
                        ? NotificationSubscriptionGrantSql.LockBindingMySql : NotificationSubscriptionGrantSql.LockBindingSqlServer,
                    NotificationPlatformSqlParameters.Create(("TenantScopeKey", "host"), ("UserId", user.Id), ("AppId", appId)), token);
                await WeChatMiniProgramBindingService.InvalidateBindingAuthorizationAsync(
                    sp.GetRequiredService<ICommandExecutor>(), bindingId, token);
                return 0;
            }, cancellationToken);
        }
        await ResolveAsync(oldIdentity, "available");
        Assert.AreEqual(0, await ReserveAsync(Guid.CreateVersion7(), 1), "身份切换后旧许可及预留均不能复活。");
        await ReportAsync("accepted", reportId);
        Assert.AreEqual(0, await ReserveAsync(Guid.CreateVersion7(), 1), "旧账号接受重放不能授权新绑定。");
        await ReportAsync("accepted", Guid.CreateVersion7());
        Assert.AreEqual(1, await ReserveAsync(Guid.CreateVersion7(), 1), "新身份必须重新明确同意。");

        var revokeProducer = $"tests.grant.{Guid.NewGuid():N}"[..28];
        const string revokeScene = "cancel.reserved";
        await NotificationProfileBindingAssertions.CreateAndPublishBindingAsync(client, token,
            $"gr-{Guid.NewGuid():N}"[..20], revokeProducer, revokeScene, profile.ProfileKey, cancellationToken);
        await NotificationProfileBindingAssertions.CreateProducerSceneAsync(client, token, revokeProducer, revokeScene,
            TestNotificationProvider.ChannelKey, cancellationToken);
        var revokeTemplate = $"grt-{Guid.NewGuid():N}"[..20];
        await NotificationProfileBindingAssertions.CreateAndPublishTestTemplateAsync(client, token, revokeTemplate, cancellationToken);
        var revokeRoute = new NotificationDeliveryWorkerAssertions.RouteFixture(revokeProducer, revokeScene, revokeTemplate);
        foreach (var withdrawConsent in new[] { false, true })
        {
            var intent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, token, revokeRoute, user.Id,
                $"grant-revoke-{withdrawConsent}", cancellationToken);
            var delivery = await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, intent.Id, cancellationToken);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(
                    new SqlStatement("tests.notifications.claim_grant_before_revoke", """
                        UPDATE fn_notifications_delivery SET LeaseGeneration = 1, LeaseOwnerKey = 'crashed-before-boundary',
                            LeaseExpiresAtUtc = @Until WHERE Id = @Id
                        """, SqlDataScope.Global), NotificationPlatformSqlParameters.Create(("Id", delivery.Id),
                        ("Until", DateTimeOffset.UtcNow.AddMinutes(5))), cancellationToken);
            }
            await ReportAsync("accepted", Guid.CreateVersion7());
            Assert.AreEqual(1, await ReserveAsync(delivery.Id, 1));
            if (withdrawConsent) await ReportAsync("rejected", Guid.CreateVersion7());
            using var revoked = await SendAsync(client, token, HttpMethod.Post, $"/api/v1/notifications/intents/{intent.Id:D}/revoke",
                new RevokeNotificationIntentRequest(intent.Revision, "cancel-unused-grant"), cancellationToken);
            Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode);
            var next = Guid.CreateVersion7();
            Assert.AreEqual(withdrawConsent ? 0 : 1, await ReserveAsync(next, 1),
                "撤回确定未调用的许可应释放；已撤销同意的预留不得复活。");
            if (!withdrawConsent) await ResolveAsync(next, "consumed");
        }

        async Task<string> ReportAsync(string status, Guid authorizationId)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            var service = new WeChatMiniProgramBindingService(sp.GetRequiredService<IQueryExecutor>(),
                sp.GetRequiredService<ICommandExecutor>(), sp.GetRequiredService<ICommandTransaction>(), sp.GetRequiredService<ICurrentTenant>(),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<IIdGenerator>(), null!, null!, null!, null!, sp.GetRequiredService<IOptions<DatabaseOptions>>());
            var result = await service.RecordSubscriptionAsync(user.Id, appId, new("template", status, authorizationId), cancellationToken);
            Assert.IsTrue(result.IsSuccess);
            return result.Value!.Subscriptions.Single(row => row.TemplateId == "template").StatusKey;
        }
        async Task<int> ReserveAsync(Guid deliveryId, long generation, string tenantScopeKey = "host")
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            return await new NotificationSubscriptionGovernor(sp.GetRequiredService<IQueryExecutor>(), sp.GetRequiredService<ICommandExecutor>(),
                sp.GetRequiredService<ICommandTransaction>(), sp.GetRequiredService<IOptions<DatabaseOptions>>())
                .ReserveAsync(deliveryId, generation, tenantScopeKey, user.Id, appId, "template", profileId, cancellationToken);
        }
        async Task ResolveAsync(Guid deliveryId, string state, long generation = 1)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(NotificationSubscriptionGrantSql.Resolve,
                NotificationPlatformSqlParameters.Create(("DeliveryId", deliveryId), ("LeaseGeneration", generation), ("StateKey", state)), cancellationToken);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string token, HttpMethod method,
        string path, object? body, CancellationToken cancellationToken)
    {
        using var request = NotificationProfileBindingAssertions.CreateBearerJsonRequest(method, path, token, body);
        return await client.SendAsync(request, cancellationToken);
    }
}
