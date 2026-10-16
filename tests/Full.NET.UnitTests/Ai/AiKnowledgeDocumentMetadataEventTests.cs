using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.IncrementalKnowledgeIndexing;
using Full.NET.Modules.Ai.Features.ManageKnowledgeDocuments;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>元数据事件只修复来源意图；事务、可信范围与有费用执行必须保持分离。</summary>
[TestClass]
public sealed class AiKnowledgeDocumentMetadataEventTests
{
    [TestMethod]
    [DataRow(false, "success")]
    [DataRow(true, "success")]
    [DataRow(false, "missing")]
    [DataRow(true, "missing")]
    [DataRow(false, "invalid")]
    [DataRow(true, "invalid")]
    [DataRow(false, "conflict")]
    [DataRow(true, "conflict")]
    [DataRow(false, "outbox")]
    [DataRow(true, "outbox")]
    [DataRow(false, "scope-query")]
    [DataRow(true, "scope-query")]
    [DataRow(false, "scope-write")]
    [DataRow(true, "scope-write")]
    [DataRow(false, "scope-outbox")]
    [DataRow(true, "scope-outbox")]
    [DataRow(false, "cancel")]
    [DataRow(true, "cancel")]
    public async Task Metadata_update_publishes_only_inside_successful_scoped_transaction(bool scoped, string scenario)
    {
        var tenant = Tenant(scoped); var originalTenant = tenant.Id;
        var knowledge = Guid.CreateVersion7(); var document = Guid.CreateVersion7();
        var inside = false; var events = new List<AiKnowledgeDocumentMetadataUpdated>();
        void ChangeScope() => tenant.SetTenant(new(Guid.CreateVersion7(), "changed", "Changed"));
        var query = new Query { Document = () =>
        {
            if (scenario == "scope-query") ChangeScope();
            return scenario == "missing" ? null : new() { Id = document, KnowledgeBaseId = knowledge, Version = 2, Title = "更新" };
        } };
        var command = Substitute.For<ICommandExecutor>();
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.IsTrue(inside);
            if (scenario == "scope-write") ChangeScope();
            return scenario == "conflict" ? 0 : 1;
        });
        var transaction = Substitute.For<ICommandTransaction>();
        transaction.ExecuteResultAsync(Arg.Any<Func<CancellationToken, Task<Result<AiKnowledgeDocumentResponse>>>>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            inside = true;
            try { return await (call.Arg<Func<CancellationToken, Task<Result<AiKnowledgeDocumentResponse>>>>() ?? throw new InvalidOperationException("Expected transaction callback"))(call.Arg<CancellationToken>()); }
            finally { inside = false; }
        });
        var outbox = Substitute.For<IOutboxWriter>();
        outbox.AddAsync(AiKnowledgeDocumentMetadataUpdated.EventType, 1, Arg.Any<AiKnowledgeDocumentMetadataUpdated>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.IsTrue(inside); var change = call.Arg<AiKnowledgeDocumentMetadataUpdated>() ?? throw new InvalidOperationException("Expected metadata payload");
            Assert.AreEqual(originalTenant, change.TenantId); Assert.AreEqual(document, change.DocumentId);
            Assert.AreEqual(knowledge, change.KnowledgeBaseId); Assert.AreEqual(2, change.MetadataVersion);
            if (scenario == "outbox") throw new IOException("Controlled Outbox failure");
            if (scenario == "scope-outbox") ChangeScope();
            events.Add(change); return Task.CompletedTask;
        });
        var access = new AiKnowledgeReadAccess(Substitute.For<IActiveRoleDirectory>(), Substitute.For<ICurrentTenantOrganizationUnitDirectory>(), tenant);
        var service = new AiKnowledgeDocumentService(query, command, transaction, tenant, Options.Create(new DatabaseOptions()),
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), access, outbox);
        var request = new UpdateAiKnowledgeDocumentRequest(scenario == "invalid" ? " " : "更新", null, 1);
        using var stop = new CancellationTokenSource();
        if (scenario == "cancel") stop.Cancel();
        if (scenario == "cancel") await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.UpdateAsync(knowledge, document, Guid.CreateVersion7(), request, stop.Token));
        else if (scenario == "outbox") await Assert.ThrowsExactlyAsync<IOException>(() => service.UpdateAsync(knowledge, document, Guid.CreateVersion7(), request, default));
        else if (scenario.StartsWith("scope-", StringComparison.Ordinal)) await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.UpdateAsync(knowledge, document, Guid.CreateVersion7(), request, default));
        else
        {
            var result = await service.UpdateAsync(knowledge, document, Guid.CreateVersion7(), request, default);
            Assert.AreEqual(scenario == "success", result.IsSuccess);
        }
        Assert.IsFalse(inside);
        Assert.AreEqual(scenario is "success" or "scope-outbox" ? 1 : 0, events.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Persistent_metadata_event_has_deterministic_roundtrip_without_document_text(bool scoped)
    {
        var change = new AiKnowledgeDocumentMetadataUpdated(Guid.CreateVersion7(), Guid.CreateVersion7(), 9, scoped ? Guid.CreateVersion7() : null);
        var bytes = MemoryPackSerializer.Serialize(change);
        CollectionAssert.AreEqual(bytes, MemoryPackSerializer.Serialize(change));
        Assert.AreEqual(change, MemoryPackSerializer.Deserialize<AiKnowledgeDocumentMetadataUpdated>(bytes));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Delivery_creates_fresh_scope_and_rechecks_current_source(bool scoped, bool mysql)
    {
        var change = new AiKnowledgeDocumentMetadataUpdated(Guid.CreateVersion7(), Guid.CreateVersion7(), 7, scoped ? Guid.CreateVersion7() : null);
        var seen = new List<Guid?>(); var writes = 0; AiKnowledgeSourceConfirmed? source = new(change.KnowledgeBaseId, change.DocumentId, Guid.CreateVersion7(), change.TenantId);
        var services = new ServiceCollection();
        services.AddScoped<CurrentTenantAccessor>(); services.AddScoped<ICurrentTenant>(p => p.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(p => p.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton(Options.Create(new DatabaseOptions { Provider = mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer }));
        services.AddScoped<IQueryExecutor>(p => new Query { Source = (sql, parameters) =>
        {
            var current = p.GetRequiredService<ICurrentTenant>(); seen.Add(current.Id);
            Assert.AreEqual(change.TenantId, current.Id); Assert.AreEqual(!scoped, current.IsHost);
            Assert.AreSame(AiKnowledgeMetadataSourceSql.For(mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer, scoped), sql);
            Assert.AreEqual(change.MetadataVersion, parameters["MetadataVersion"]);
            return source;
        } });
        services.AddScoped(p =>
        {
            var command = Substitute.For<ICommandExecutor>(); command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { writes++; return 1; });
            var tx = Substitute.For<ICommandTransaction>(); tx.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>()).Returns(c => (c.Arg<Func<CancellationToken, Task<bool>>>() ?? throw new InvalidOperationException("Expected transaction callback"))(c.Arg<CancellationToken>()));
            return new AiKnowledgeIncrementalIndexWriter(new Query(), command, tx, p.GetRequiredService<ICurrentTenant>(), p.GetRequiredService<IOptions<DatabaseOptions>>(), Substitute.For<IClock>(), Substitute.For<IIdGenerator>());
        });
        using var provider = services.BuildServiceProvider();
        var serializer = Substitute.For<IIntegrationEventSerializer>(); serializer.Deserialize<AiKnowledgeDocumentMetadataUpdated>(Arg.Any<ReadOnlyMemory<byte>>()).Returns(_ => change);
        var handler = new AiKnowledgeDocumentMetadataUpdatedHandler(serializer, provider.GetRequiredService<IServiceScopeFactory>());
        var context = new IntegrationEventContext(Guid.CreateVersion7(), handler.EventType, 1, change.TenantId, null, DateTimeOffset.UtcNow);
        await handler.HandleAsync(context, ReadOnlyMemory<byte>.Empty, default);
        Assert.AreEqual(2, writes);
        // 已删除、禁用或未来版本均无可用来源；不能借旧通知绕过权威检查。
        source = null; await handler.HandleAsync(context, ReadOnlyMemory<byte>.Empty, default); Assert.AreEqual(2, writes);
        source = new(change.KnowledgeBaseId, Guid.CreateVersion7(), Guid.CreateVersion7(), change.TenantId);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(context, ReadOnlyMemory<byte>.Empty, default));
        Assert.AreEqual(2, writes); Assert.AreEqual(3, seen.Count);
    }

    [TestMethod]
    [DataRow("message")]
    [DataRow("type")]
    [DataRow("schema")]
    [DataRow("tenant")]
    [DataRow("knowledge")]
    [DataRow("document")]
    [DataRow("version")]
    [DataRow("empty-tenant")]
    public async Task Invalid_envelope_or_identity_cannot_open_a_scope(string scenario)
    {
        var change = new AiKnowledgeDocumentMetadataUpdated(Guid.CreateVersion7(), Guid.CreateVersion7(), 2, null);
        change = scenario switch { "knowledge" => change with { KnowledgeBaseId = Guid.Empty }, "document" => change with { DocumentId = Guid.Empty }, "version" => change with { MetadataVersion = 0 }, "empty-tenant" => change with { TenantId = Guid.Empty }, _ => change };
        var serializer = Substitute.For<IIntegrationEventSerializer>(); serializer.Deserialize<AiKnowledgeDocumentMetadataUpdated>(Arg.Any<ReadOnlyMemory<byte>>()).Returns(change);
        var scopes = Substitute.For<IServiceScopeFactory>(); var handler = new AiKnowledgeDocumentMetadataUpdatedHandler(serializer, scopes);
        var context = new IntegrationEventContext(scenario == "message" ? Guid.Empty : Guid.CreateVersion7(), scenario == "type" ? "other" : handler.EventType,
            scenario == "schema" ? 2 : 1, scenario == "tenant" ? Guid.CreateVersion7() : change.TenantId, null, DateTimeOffset.UtcNow);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(context, ReadOnlyMemory<byte>.Empty, default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(ReadOnlyMemory<byte>.Empty, default));
        Assert.AreEqual(0, scopes.ReceivedCalls().Count());
    }

    private static CurrentTenantAccessor Tenant(bool scoped) { var current = new CurrentTenantAccessor(); if (scoped) current.SetTenant(new(Guid.CreateVersion7(), "acme", "Acme")); else current.SetHost(); return current; }
    private sealed class Query : IQueryExecutor
    {
        internal Func<AiKnowledgeDocumentRecord?> Document { get; init; } = () => null;
        internal Func<SqlStatement, IReadOnlyDictionary<string, object?>, AiKnowledgeSourceConfirmed?> Source { get; init; } = (_, _) => null;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected unbounded query");
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(typeof(T) == typeof(int) ? (T?)(object)2 : typeof(T) == typeof(AiKnowledgeDocumentRecord) ? (T?)(object?)Document() : (T?)(object?)Source(statement, (IReadOnlyDictionary<string, object?>)parameters!));
    }
}
