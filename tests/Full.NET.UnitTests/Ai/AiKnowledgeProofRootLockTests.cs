using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>载荷预读的根锁顺序和拒绝边界，防止事务外载荷锁与最终证明反向等待。</summary>
[TestClass]
public sealed class AiKnowledgeProofRootLockTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, false, true)] [DataRow(DatabaseProvider.SqlServer, true, true)]
    [DataRow(DatabaseProvider.MySql, false, true)] [DataRow(DatabaseProvider.MySql, true, true)]
    [DataRow(DatabaseProvider.SqlServer, false, false)] [DataRow(DatabaseProvider.SqlServer, true, false)]
    [DataRow(DatabaseProvider.MySql, false, false)] [DataRow(DatabaseProvider.MySql, true, false)]
    public async Task Payload_waits_for_document_root_before_any_task_payload_lock(DatabaseProvider provider, bool scoped, bool found)
    {
        var tenant = new CurrentTenantAccessor(); if (scoped) tenant.SetTenant(new(Guid.CreateVersion7(), "test", "测试")); else tenant.SetHost();
        var binding = new AiKnowledgeIndexTaskBinding(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1, Guid.CreateVersion7());
        var query = Substitute.For<IQueryExecutor>(); var transaction = Substitute.For<ICommandTransaction>();
        var state = Substitute.For<IDataTransactionState>(); var inside = false; state.HasTransaction.Returns(_ => inside);
        var calls = new List<string>(); var payload = new AiKnowledgeIndexVectorRecord { Id = Guid.CreateVersion7(), Payload = [1, 2, 3] };
        transaction.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<AiKnowledgeIndexVectorRecord?>>>(), Arg.Any<CancellationToken>()).Returns(async call =>
        { inside = true; try { return await call.ArgAt<Func<CancellationToken, Task<AiKnowledgeIndexVectorRecord?>>>(0)(call.ArgAt<CancellationToken>(1)); } finally { inside = false; } });
        query.QuerySingleOrDefaultAsync<Guid>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.IsTrue(inside); var sql = call.ArgAt<SqlStatement>(0);
            Assert.AreEqual(AiKnowledgePublicationSql.LockFor(provider, scoped), sql); calls.Add("root");
            return found ? binding.DocumentId : Guid.Empty;
        });
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexVectorRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            Assert.IsTrue(inside); CollectionAssert.AreEqual(new[] { "root" }, calls); calls.Add("payload");
            Assert.AreEqual(scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, call.ArgAt<SqlStatement>(0).Scope); return payload;
        });
        var clock = Substitute.For<IClock>();
        var store = new AiKnowledgeCorrectionActivationStore(query, Substitute.For<ICommandExecutor>(), transaction, state, tenant, clock,
            Substitute.For<IIdGenerator>(), Options.Create(new DatabaseOptions { Provider = provider }), new AiKnowledgeIndexTaskScope(tenant, clock), null!, Substitute.For<IBackgroundSessionAuthorization>());
        var result = await store.ReadPayloadUnderRootAsync(binding, new() { ["KnowledgeBaseId"] = binding.KnowledgeBaseId, ["DocumentId"] = binding.DocumentId }, default);
        Assert.AreSame(found ? payload : null, result); Assert.IsFalse(inside);
        CollectionAssert.AreEqual(found ? new[] { "root", "payload" } : new[] { "root" }, calls);
    }
}
