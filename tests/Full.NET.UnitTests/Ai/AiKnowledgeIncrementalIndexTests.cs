using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Ai.Features.ManageKnowledgeVersions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Ai;

/// <summary>增量意图只保存可靠来源变化；不能通过后台事件取得会话或产生模型费用。</summary>
[TestClass]
public sealed class AiKnowledgeIncrementalIndexTests
{
    [TestMethod]
    [DataRow(false, "allow")]
    [DataRow(true, "allow")]
    [DataRow(false, "deny")]
    [DataRow(true, "deny")]
    [DataRow(false, "revoke")]
    [DataRow(true, "revoke")]
    [DataRow(false, "session")]
    [DataRow(true, "session")]
    [DataRow(false, "document")]
    [DataRow(true, "document")]
    public async Task Version_listing_checks_index_permission_after_query_and_masks_without_blocking_metadata(bool scoped, string scenario)
    {
        var tenant = Tenant(scoped);
        var actor = Guid.CreateVersion7();
        var readFinished = false;
        var granted = scenario != "deny";
        var query = new VersionQueries(() => readFinished && scenario == "document" ? null : new AiKnowledgeDocumentRecord(), () =>
        {
            readFinished = true;
            if (scenario == "revoke") granted = false;
            return [new() { ProcessingStatus = "index_admitted" }, new() { ProcessingStatus = "awaiting_index_confirmation" }];
        });
        var authorization = Substitute.For<ICurrentSessionAuthorization>(); authorization.UsePermissionSetChecks();
        authorization.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.IsTrue(readFinished);
            return scenario == "session" || (!granted && call.ArgAt<string>(0) == AiKnowledgeDocumentPermissions.IndexRead)
                ? null : new AuthorizedSessionActor(actor, tenant.Id, Guid.CreateVersion7());
        });
        var roles = Substitute.For<IActiveRoleDirectory>();
        roles.ListActiveRoleIdsForUserAsync(actor, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        var organizations = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
        organizations.ListActiveUnitIdsForUserAsync(actor, Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        var guard = new AiKnowledgePermissionGuard(authorization, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>());
        var store = new AiKnowledgeImportStore(query, Substitute.For<ICommandExecutor>(), Substitute.For<ICommandTransaction>(), tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Options.Create(new DatabaseOptions()), Substitute.For<IOutboxWriter>());
        var reader = new AiKnowledgeVersionReader(store, query, new AiKnowledgeReadAccess(roles, organizations, tenant), guard, null!, null!, null!);
        var result = await reader.ListAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), actor, 1, 20, CancellationToken.None);
        if (scenario is "session" or "document")
        {
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(scenario == "session" ? "authorization.permission_denied" : "ai.knowledge.document_not_found", result.Error!.Code);
            return;
        }
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(2L, result.Value!.Total);
        CollectionAssert.AreEqual(scenario == "allow" ? new[] { "index_admitted", "awaiting_index_confirmation" } : new[] { "pending_processing", "pending_processing" }, result.Value.Items.Select(row => row.ProcessingStatus).ToArray());
    }

    [TestMethod]
    public void Event_contract_uses_valid_legacy_topic_and_static_memorypack_roundtrip()
    {
        var topic = Full.NET.Messaging.Abstractions.IntegrationEventTopicDefinition.Create(AiKnowledgeSourceConfirmed.TopicCode,
            AiKnowledgeSourceConfirmed.EventType, 1, Full.NET.Messaging.Abstractions.EventDeliveryOwner.LegacyPolling);
        Assert.AreEqual(AiKnowledgeSourceConfirmed.EventType, topic.EventType);
        var source = new AiKnowledgeSourceConfirmed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.AreEqual(source, MemoryPack.MemoryPackSerializer.Deserialize<AiKnowledgeSourceConfirmed>(MemoryPack.MemoryPackSerializer.Serialize(source)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Confirmed_import_emits_once_inside_completion_transaction(bool scoped)
    {
        var tenant = Tenant(scoped);
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var outbox = Substitute.For<IOutboxWriter>();
        var entered = false;
        var transaction = Transaction(() => entered = true, () => entered = false);
        var operation = new AiKnowledgeImportRecord { Id = Guid.CreateVersion7(), DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7() };
        query.QuerySingleOrDefaultAsync<AiKnowledgeImportRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(operation);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1, 0);
        var events = 0;
        outbox.AddAsync(AiKnowledgeSourceConfirmed.EventType, 1, Arg.Any<AiKnowledgeSourceConfirmed>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.IsTrue(entered);
            var payload = call.Arg<AiKnowledgeSourceConfirmed>() ?? throw new InvalidOperationException("Expected event payload.");
            Assert.AreEqual(operation.VersionId, payload.VersionId);
            Assert.AreEqual(tenant.Id, payload.TenantId);
            events++;
            return Task.CompletedTask;
        });
        var store = new AiKnowledgeImportStore(query, command, transaction, tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Options.Create(new DatabaseOptions()), outbox);
        var parameters = store.Parameters(Guid.CreateVersion7(), operation.DocumentId, Guid.CreateVersion7());
        parameters["OperationId"] = operation.Id;
        Assert.IsTrue(await store.StateAsync(parameters, "completed", null, CancellationToken.None));
        Assert.IsFalse(await store.StateAsync(parameters, "completed", null, CancellationToken.None));
        Assert.AreEqual(1, events);
        Assert.IsFalse(entered);
    }

    [TestMethod]
    public async Task Outbox_failure_propagates_through_completion_transaction()
    {
        var tenant = Tenant(false);
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var outbox = Substitute.For<IOutboxWriter>();
        var left = false;
        var transaction = Transaction(() => { }, () => left = true);
        query.QuerySingleOrDefaultAsync<AiKnowledgeImportRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new AiKnowledgeImportRecord { VersionId = Guid.CreateVersion7(), DocumentId = Guid.CreateVersion7() });
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        outbox.AddAsync(AiKnowledgeSourceConfirmed.EventType, 1, Arg.Any<AiKnowledgeSourceConfirmed>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("Outbox unavailable")));
        var store = new AiKnowledgeImportStore(query, command, transaction, tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), Options.Create(new DatabaseOptions()), outbox);
        await Assert.ThrowsExactlyAsync<IOException>(() => store.StateAsync(store.Parameters(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()), "completed", null, CancellationToken.None));
        Assert.IsTrue(left);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cross_scope_event_is_rejected_before_any_database_work(bool scoped)
    {
        var tenant = Tenant(scoped);
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var writer = new AiKnowledgeIncrementalIndexWriter(query, command, Substitute.For<ICommandTransaction>(), tenant,
            Options.Create(new DatabaseOptions()), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.ApplyAsync(new AiKnowledgeSourceConfirmed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), scoped ? null : Guid.CreateVersion7()), CancellationToken.None));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_or_obsolete_authority_does_not_create_incremental_work()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var writer = new AiKnowledgeIncrementalIndexWriter(query, command, Transaction(() => { }, () => { }), Tenant(false),
            Options.Create(new DatabaseOptions()), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        await writer.ApplyAsync(new AiKnowledgeSourceConfirmed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), null), CancellationToken.None);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    // 内部投影集合使用具体夹具，避免动态代理要求扩大生产程序集的可见范围。
    private sealed class VersionQueries(Func<AiKnowledgeDocumentRecord?> document, Func<AiKnowledgeVersionRecord[]> rows) : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<T>)(object)rows());
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult(typeof(T) == typeof(long) ? (T?)(object)2L : (T?)(object?)document());
    }

    private static CurrentTenantAccessor Tenant(bool scoped)
    {
        var tenant = new CurrentTenantAccessor();
        if (scoped) tenant.SetTenant(new TenantContext(Guid.CreateVersion7(), "acme", "Acme")); else tenant.SetHost();
        return tenant;
    }

    private static ICommandTransaction Transaction(Action enter, Action leave)
    {
        var transaction = Substitute.For<ICommandTransaction>();
        transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            enter();
            try
            {
                var action = call.Arg<Func<CancellationToken, Task<bool>>>() ?? throw new InvalidOperationException("Expected transaction callback.");
                return await action(call.Arg<CancellationToken>());
            }
            finally { leave(); }
        });
        return transaction;
    }
}
