using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Features;
using Full.NET.Modules.Notifications.Features.ProjectWorkflowNotifications;
using Full.NET.Modules.Notifications.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class WorkflowNotificationTemplateProvisionerTests
{
    [TestMethod]
    [DataRow("workflow.todo.assigned")]
    [DataRow("workflow.instance.completed")]
    [DataRow("workflow.instance.rejected")]
    [DataRow("workflow.instance.cancelled")]
    public void Catalog_contains_each_workflow_projection_template(string templateKey)
    {
        var found = WorkflowNotificationTemplateCatalog.TryGet(templateKey, out var definition);

        Assert.IsTrue(found);
        Assert.IsNotNull(definition);
        Assert.AreEqual(templateKey, definition.TemplateKey);
    }

    [TestMethod]
    public async Task Missing_tenant_template_is_created_versioned_and_published_atomically()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var transaction = new RecordingTransaction();
        var templateId = Guid.CreateVersion7();
        var versionId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        query.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
                NotificationPlatformSql.FindTemplateByKey,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((NotificationTemplateRecord?)null);
        command.ExecuteAsync(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(templateId, versionId);
        var service = new WorkflowNotificationTemplateProvisioner(
            query,
            command,
            transaction,
            Substitute.For<IClock>(),
            ids);

        await service.EnsurePublishedAsync(
            NotificationInboxScope.FromTrustedTenantId(tenantId),
            actorUserId,
            "workflow.todo.assigned",
            CancellationToken.None);

        Assert.AreEqual(1, transaction.ExecutionCount);
        await command.Received(1).ExecuteAsync(
            NotificationPlatformSql.InsertTemplateTenant,
            Arg.Is<object?>(value => HasParameter(value, "TenantId", tenantId)),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(
            NotificationPlatformSql.InsertTemplateVersion,
            Arg.Is<object?>(value => HasParameter(value, "Id", versionId)),
            Arg.Any<CancellationToken>());
        await command.Received(1).ExecuteAsync(
            NotificationPlatformSql.PublishTemplate,
            Arg.Is<object?>(value => HasParameter(value, "LatestPublishedVersionId", versionId)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Existing_published_template_is_reused_without_writes()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
                NotificationPlatformSql.FindTemplateByKey,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateTemplate(Guid.CreateVersion7()));
        var transaction = new RecordingTransaction();
        var service = new WorkflowNotificationTemplateProvisioner(
            query,
            command,
            transaction,
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        await service.EnsurePublishedAsync(
            NotificationInboxScope.FromTrustedTenantId(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            "workflow.instance.completed",
            CancellationToken.None);

        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
        Assert.AreEqual(0, transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task Existing_unpublished_template_fails_closed_without_overwriting_admin_draft()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
                NotificationPlatformSql.FindTemplateByKey,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateTemplate(null));
        var service = new WorkflowNotificationTemplateProvisioner(
            query,
            command,
            new RecordingTransaction(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            service.EnsurePublishedAsync(
                NotificationInboxScope.FromTrustedTenantId(Guid.CreateVersion7()),
                Guid.CreateVersion7(),
                "workflow.instance.rejected",
                CancellationToken.None));

        Assert.AreEqual("notifications.template_not_published", exception.Message);
        await command.DidNotReceive().ExecuteAsync(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Concurrent_initializer_that_publishes_first_is_reused_without_duplicate_version()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<NotificationTemplateRecord>(
                NotificationPlatformSql.FindTemplateByKey,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((NotificationTemplateRecord?)null, CreateTemplate(Guid.CreateVersion7()));
        command.ExecuteAsync(
                NotificationPlatformSql.InsertTemplateTenant,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        var service = new WorkflowNotificationTemplateProvisioner(
            query,
            command,
            new RecordingTransaction(),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>());

        await service.EnsurePublishedAsync(
            NotificationInboxScope.FromTrustedTenantId(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            "workflow.instance.cancelled",
            CancellationToken.None);

        await command.DidNotReceive().ExecuteAsync(
            NotificationPlatformSql.InsertTemplateVersion,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    private static NotificationTemplateRecord CreateTemplate(Guid? publishedVersionId) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "tenant",
            $"tenant:{Guid.CreateVersion7():N}",
            "workflow.instance.completed",
            "inbox",
            "transactional",
            "标题",
            "{\"text\":\"正文\"}",
            "{\"schemaVersion\":1,\"parameters\":[]}",
            1,
            publishedVersionId,
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow,
            null,
            1);

    private static bool HasParameter(object? value, string name, object expected) =>
        value is IReadOnlyDictionary<string, object?> parameters
        && parameters.TryGetValue(name, out var actual)
        && Equals(expected, actual);

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return await action(cancellationToken);
        }
    }
}
