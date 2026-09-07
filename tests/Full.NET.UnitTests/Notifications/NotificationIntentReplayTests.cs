using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Features;
using Full.NET.Modules.Notifications.Features.CreateNotificationIntents;
using Full.NET.Modules.Notifications.Features.ManageTemplates;
using Full.NET.Modules.Notifications.Persistence;
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
    /// <summary>返回已持久化快照，不为内部记录生成动态代理。</summary>
    private sealed class ReplayQueries : IQueryExecutor
    {
        public Dictionary<Type, object> Rows { get; } = [];
        public IReadOnlyList<NotificationRecipientRecord> Recipients { get; set; } = [];

        /// <summary>按投影类型返回不可变的原请求记录。</summary>
        /// <typeparam name="T">查询投影类型。</typeparam>
        /// <param name="statement">被测查询。</param>
        /// <param name="parameters">查询参数。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.TryGetValue(typeof(T), out var row) ? (T)row : default);

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
