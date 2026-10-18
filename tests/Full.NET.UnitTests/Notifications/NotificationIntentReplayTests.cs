using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Features;
using Full.NET.Modules.Notifications.Features.CreateNotificationIntents;
using Full.NET.Modules.Notifications.Features.ManageTemplates;
using Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

/// <summary>重放只依赖首次受理快照，当前模板停用或升级不能改变既有请求的幂等结果。</summary>
[TestClass]
public sealed class NotificationIntentReplayTests
{
    /// <summary>当前模板无可选版本时仍可重放原请求，改换模板键必须冲突。</summary>
    /// <param name="templateKey">重试提交的模板键。</param>
    /// <param name="succeeds">是否为原始请求。</param>
    [TestMethod]
    [DataRow("receipt", true)]
    [DataRow("another-template", false)]
    public async Task Replay_uses_persisted_template_version(string templateKey, bool succeeds)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var queries = new ReplayQueries();
        var commands = Substitute.For<ICommandExecutor>();
        queries.Rows[typeof(NotificationIntentRecord)] = new NotificationIntentRecord(intentId, null, "host", "host", "test", "receipt", "request-1",
                versionId, null, "transactional", "single", "{}", "{}", "accepted", userId, now, 1);
        queries.Rows[typeof(NotificationTemplateVersionRecord)] = new NotificationTemplateVersionRecord(versionId, templateId, "zh-CN", 1, 1,
                "subject", "{}", "{\"schemaVersion\":1,\"parameters\":[{\"name\":\"value\",\"typeKey\":\"string\",\"required\":false,\"maxLength\":100}]}", "public", "hash", userId, now);
        queries.Rows[typeof(NotificationTemplateRecord)] = new NotificationTemplateRecord(templateId, null, "host", "host", "receipt", "zh-CN", "zh-CN",
                "inbox", "transactional", "changed subject", "{}", "[]", 2, null, userId, now, now, 2);
        queries.Recipients = [new NotificationRecipientRecord(Guid.NewGuid(), intentId, "user", userId.ToString("N"),
            userId, null, "resolved", now)];
        var service = new NotificationIntentService(
            queries, commands, Substitute.For<ICommandTransaction>(), Substitute.For<IOutboxWriter>(),
            null!, new NotificationTemplateSelector(queries), null!, Substitute.For<ICurrentTenant>(),
            null!, null!, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
            NullLogger<NotificationIntentService>.Instance);
        using var parameters = JsonDocument.Parse("{}");
        var request = new CreateNotificationIntentRequest("test", "receipt", templateKey,
            [new NotificationRecipientInput("user", userId.ToString("D"))], parameters.RootElement, "request-1");

        var result = await service.CreateForTrustedEventAsync(NotificationInboxScope.FromTrustedTenantId(null),
            userId, request, CancellationToken.None);

        Assert.AreEqual(succeeds, result.IsSuccess);
        if (succeeds)
        {
            Assert.IsFalse(result.Value!.Created);
            Assert.AreEqual(intentId, result.Value.Intent.Id);
            Assert.AreEqual(versionId, result.Value.Intent.TemplateVersionId);
        }
        else
        {
            Assert.AreEqual(NotificationsErrorCodes.IntentIdempotencyConflict, result.Error!.Code);
        }

        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
    /// <summary>Workflow 重投必须使用已受理快照，不能再次预置当前未发布模板。</summary>
    [TestMethod]
    [DataRow("completed", false, false)]
    [DataRow("completed", true, false)]
    [DataRow("rejected", false, false)]
    [DataRow("rejected", true, false)]
    [DataRow("cancelled", false, false)]
    [DataRow("cancelled", true, false)]
    [DataRow("completed", false, true)]
    [DataRow("completed", true, true)]
    [DataRow("rejected", false, true)]
    [DataRow("rejected", true, true)]
    [DataRow("cancelled", false, true)]
    [DataRow("cancelled", true, true)]
    public async Task Workflow_replay_survives_current_unpublished_template(string kind, bool tenantScoped, bool conflicts)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7();
        var instanceId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        var templateId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var intentId = Guid.CreateVersion7();
        var tenantId = tenantScoped ? Guid.CreateVersion7() : (Guid?)null;
        var scope = NotificationInboxScope.FromTrustedTenantId(tenantId);
        var request = kind switch
        {
            "completed" => WorkflowNotificationRequestFactory.Create(messageId,
                new WorkflowInstanceCompletedIntegrationEvent(instanceId, userId, "demo.enterprise_request", "request-1", now)),
            "rejected" => WorkflowNotificationRequestFactory.Create(messageId,
                new WorkflowInstanceRejectedIntegrationEvent(instanceId, userId, "demo.enterprise_request", "request-1", now)),
            _ => WorkflowNotificationRequestFactory.Create(messageId,
                new WorkflowInstanceCancelledIntegrationEvent(instanceId, userId, "demo.enterprise_request", "request-1", now))
        };
        Assert.IsTrue(WorkflowNotificationTemplateCatalog.TryGet(request.TemplateKey, out var definition));
        var draft = NotificationTemplateCompiler.NormalizeDraft(definition!.Subject, definition.Body, definition.ParameterSchema).Value!;
        var schema = NotificationTemplateCompiler.NormalizeSchema(definition.ParameterSchema).Value!;
        var snapshot = NotificationTemplateCompiler.ValidateAndSnapshotParameters(schema, request.Parameters).Value!;
        var queries = new ReplayQueries();
        queries.Rows[typeof(NotificationIntentRecord)] = new NotificationIntentRecord(intentId, tenantId,
            scope.TenantScopeKey, tenantScoped ? "tenant" : "host", request.ProducerKey, request.SceneKey, request.IdempotencyKey,
            versionId, null, "transactional", "single", "{}", snapshot, "accepted", userId, now, 1);
        queries.Rows[typeof(NotificationTemplateVersionRecord)] = new NotificationTemplateVersionRecord(versionId, templateId,
            "zh-CN", 1, 1, draft.Subject, draft.BodyJson, draft.ParameterSchemaJson, "public", "hash", userId, now);
        queries.Rows[typeof(NotificationTemplateRecord)] = new NotificationTemplateRecord(templateId, tenantId,
            scope.TenantScopeKey, tenantScoped ? "tenant" : "host", request.TemplateKey, "zh-CN", "zh-CN", "inbox", "transactional",
            "changed subject", "{}", "[]", 2, null, userId, now, now, 2);
        queries.Recipients = [new NotificationRecipientRecord(Guid.CreateVersion7(), intentId, "user", userId.ToString("N"),
            userId, null, "resolved", now)];
        var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>();
        var clock = Substitute.For<IClock>();
        var ids = Substitute.For<IIdGenerator>();
        var tenant = Substitute.For<ICurrentTenantContextWriter>();
        tenant.IsHost.Returns(true);
        var intents = new NotificationIntentService(queries, commands, transaction, Substitute.For<IOutboxWriter>(),
            null!, new NotificationTemplateSelector(queries), null!, tenant, null!, null!, clock, ids,
            NullLogger<NotificationIntentService>.Instance);
        var projection = new WorkflowNotificationProjectionService(
            new WorkflowNotificationTemplateProvisioner(queries, commands, transaction, clock, ids), intents, tenant);

        if (conflicts)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => projection.ProjectAsync(
                tenantId, userId, request with { SceneKey = "changed-scene" }, CancellationToken.None));
            Assert.AreEqual(NotificationsErrorCodes.IntentIdempotencyConflict, error.Message);
        }
        else
        {
            await projection.ProjectAsync(tenantId, userId, request, CancellationToken.None);
        }

        Assert.AreEqual(0, commands.ReceivedCalls().Count());
        Assert.AreEqual(0, transaction.ReceivedCalls().Count());
        tenant.Received(tenantScoped ? 1 : 2).SetHost();
    }

    /// <summary>首次准备在幂等查找之后执行；失败或取消不得进入 Intent 写事务。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_first_acceptance_preparation_has_no_writes(bool cancelled)
    {
        var queries = new ReplayQueries();
        var commands = Substitute.For<ICommandExecutor>();
        var transaction = Substitute.For<ICommandTransaction>();
        var service = new NotificationIntentService(queries, commands, transaction, Substitute.For<IOutboxWriter>(),
            null!, new NotificationTemplateSelector(queries), null!, Substitute.For<ICurrentTenant>(), null!, null!,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), NullLogger<NotificationIntentService>.Instance);
        using var parameters = JsonDocument.Parse("{}");
        using var cancellation = new CancellationTokenSource();
        var request = new CreateNotificationIntentRequest("workflow", "workflow.instance.completed", "workflow.instance.completed",
            [new NotificationRecipientInput("user", Guid.CreateVersion7().ToString("N"))], parameters.RootElement, "request-1");
        var prepared = 0;
        Task Prepare(CancellationToken token)
        {
            Assert.AreEqual(cancellation.Token, token);
            CollectionAssert.AreEqual(new[] { NotificationPlatformSql.FindIntentByIdempotency.Name }, queries.Statements);
            prepared++;
            if (cancelled)
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            }
            return Task.FromException(new InvalidOperationException("test.template_unavailable"));
        }
        Task Deliver() => service.CreateForTrustedEventAsync(NotificationInboxScope.FromTrustedTenantId(null),
            Guid.CreateVersion7(), request, cancellation.Token, Prepare);
        if (cancelled)
        {
            var error = await Assert.ThrowsAsync<OperationCanceledException>(Deliver);
            Assert.AreEqual(cancellation.Token, error.CancellationToken);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(Deliver);
            Assert.AreEqual("test.template_unavailable", error.Message);
        }
        Assert.AreEqual(1, prepared);
        Assert.HasCount(1, queries.Statements);
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
        Assert.AreEqual(0, transaction.ReceivedCalls().Count());
    }

    /// <summary>返回已持久化快照，不为内部记录生成动态代理。</summary>
    private sealed class ReplayQueries : IQueryExecutor
    {
        public Dictionary<Type, object> Rows { get; } = [];
        public IReadOnlyList<NotificationRecipientRecord> Recipients { get; set; } = [];
        public List<string> Statements { get; } = [];

        /// <summary>按投影类型返回不可变的原请求记录。</summary>
        /// <typeparam name="T">查询投影类型。</typeparam>
        /// <param name="statement">被测查询。</param>
        /// <param name="parameters">查询参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement.Name);
            return Task.FromResult(Rows.TryGetValue(typeof(T), out var row) ? (T)row : default);
        }

        /// <summary>仅返回原收件人，当前模板候选及附件均为空。</summary>
        /// <typeparam name="T">查询投影类型。</typeparam>
        /// <param name="statement">被测查询。</param>
        /// <param name="parameters">查询参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(typeof(T) == typeof(NotificationRecipientRecord)
                ? (IReadOnlyList<T>)(object)Recipients : (IReadOnlyList<T>)Array.Empty<T>());
    }
}
