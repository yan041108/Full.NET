using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>继续对话验证完整业务历史；普通模型输入仍不能偷偷带入派生答案。</summary>
[TestClass]
public sealed class AiPersonalScheduleContinuationGateTests
{
    [TestMethod] [DataRow(false, DatabaseProvider.SqlServer)] [DataRow(true, DatabaseProvider.SqlServer)]
    [DataRow(false, DatabaseProvider.MySql)] [DataRow(true, DatabaseProvider.MySql)]
    public async Task Complete_business_history_uses_send_and_owner_tenant_bound_metadata_without_body(bool scoped, DatabaseProvider provider)
    {
        var f = new Fixture(scoped, provider); var bound = await f.Gate.CaptureAsync(f.History.Data.SessionId, f.History.Data.ActorUserId, default);
        Assert.HasCount(1, bound); StringAssert.Contains(f.Query.Statement!.Text, "'' AS Content");
        Assert.AreEqual(scoped ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly, f.Query.Statement.Scope);
        Assert.IsTrue(f.History.Permissions.Contains(AiChatPermissions.Send)); Assert.IsFalse(f.History.Permissions.Contains(AiChatPermissions.Read));
    }

    [TestMethod] [DataRow("calendar")] [DataRow("permission")] [DataRow("corrupt")] [DataRow("unknown")] [DataRow("owner")]
    public async Task Invalid_business_history_rejects_before_any_continuation_can_be_charged(string fault)
    {
        var f = new Fixture();
        if (fault == "calendar") f.Readable = false;
        if (fault == "permission") f.History.Denied = AiKnowledgeDocumentPermissions.SourceRead;
        if (fault == "corrupt") f.Query.Rows[0] = Message(f.Query.Rows[0], null);
        if (fault == "unknown") f.Query.Rows[0] = Message(f.Query.Rows[0], f.Query.Rows[0].KnowledgeLineage!.Replace("\"version\":8", "\"version\":9", StringComparison.Ordinal));
        if (fault == "owner") f.Query.Rows[0] = Message(f.Query.Rows[0], f.Query.Rows[0].KnowledgeLineage!.Replace(f.History.Data.ActorUserId.ToString(), Guid.CreateVersion7().ToString(), StringComparison.Ordinal));
        await Assert.ThrowsExactlyAsync<AiPersonalScheduleContinuationUnavailableException>(() => f.Gate.CaptureAsync(f.History.Data.SessionId, f.History.Data.ActorUserId, default));
    }

    [TestMethod] [DataRow("replace")] [DataRow("remove")] [DataRow("revoke")]
    public async Task Frozen_business_binding_is_rechecked_before_dispatch_and_buffered_delivery(string fault)
    {
        var f = new Fixture(); var bound = await f.Gate.CaptureAsync(f.History.Data.SessionId, f.History.Data.ActorUserId, default);
        if (fault == "remove") f.Query.Rows = [];
        if (fault == "revoke") f.Readable = false;
        if (fault == "replace")
        {
            var changed = f.History.Data with { PersonalSchedules = [f.History.Data.PersonalSchedules![0] with { Version = 3 }] };
            f.Query.Rows[0] = Message(f.Query.Rows[0], AiKnowledgeAnswerLineageCodec.Encode(changed));
        }
        await Assert.ThrowsExactlyAsync<AiPersonalScheduleContinuationUnavailableException>(() => f.Gate.RevalidateAsync(bound, f.History.Data.SessionId, f.History.Data.ActorUserId, default));
    }

    [TestMethod]
    public async Task Sixty_fifth_dependency_is_rejected_instead_of_truncated()
    {
        var f = new Fixture(); f.Query.Rows = Enumerable.Range(0, 65).Select(_ => Message(f.Query.Rows[0], f.Query.Rows[0].KnowledgeLineage, Guid.CreateVersion7())).ToArray();
        await Assert.ThrowsExactlyAsync<AiPersonalScheduleContinuationUnavailableException>(() => f.Gate.CaptureAsync(f.History.Data.SessionId, f.History.Data.ActorUserId, default));
        Assert.AreEqual(0, f.History.Queries);
    }

    [TestMethod]
    public async Task Valid_legacy_document_answer_is_omitted_without_becoming_a_business_input()
    {
        var f = new Fixture(); var old = f.History.Data with { Version = 3, PersonalSchedules = null, PersonalScheduleReferences = null };
        f.Query.Rows[0] = Message(f.Query.Rows[0], AiKnowledgeAnswerLineageCodec.Encode(old));
        Assert.HasCount(0, await f.Gate.CaptureAsync(old.SessionId, old.ActorUserId, default)); Assert.AreEqual(0, f.History.Queries);
    }

    [TestMethod]
    public async Task Source_gate_never_joins_a_caller_transaction()
    {
        var f = new Fixture(); f.History.Transactions.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<AiPersonalScheduleContinuationUnavailableException>(() => f.Gate.CaptureAsync(f.History.Data.SessionId, f.History.Data.ActorUserId, default));
        Assert.AreEqual(0, f.Query.Calls);
    }

    private static AiChatMessageRecord Message(AiChatMessageRecord original, string? json, Guid? id = null) => new()
    { Id = id ?? original.Id, SessionId = original.SessionId, RoleKey = original.RoleKey, StatusKey = original.StatusKey,
        IsKnowledgeAnswer = original.IsKnowledgeAnswer, KnowledgeLineage = json, CreatedAtUtc = original.CreatedAtUtc };

    private sealed class Fixture
    {
        internal readonly AiKnowledgeAnswerHistoryGuardTests.Fixture History;
        internal readonly MetadataQuery Query; internal readonly AiPersonalScheduleContinuationGate Gate; internal bool Readable = true;
        internal Fixture(bool scoped = false, DatabaseProvider provider = DatabaseProvider.SqlServer)
        {
            var calendar = Substitute.For<IPersonalScheduleKnowledgeSource>();
            calendar.VerifyAsync(Arg.Any<IReadOnlyCollection<PersonalScheduleKnowledgeProof>>(), Arg.Any<CancellationToken>()).Returns(_ => Readable);
            History = new(scoped, calendar);
            History.Data = History.Data with { Version = 8, PersonalSchedules = [AiPersonalScheduleAnswerTests.Schedule(History.Data.ActorUserId, History.Tenant.Id).Proof], PersonalScheduleReferences = [] };
            History.Json = AiKnowledgeAnswerLineageCodec.Encode(History.Data);
            Query = new() { Rows = [new() { Id = History.Data.MessageId, SessionId = History.Data.SessionId, RoleKey = "assistant", StatusKey = "completed", IsKnowledgeAnswer = true, KnowledgeLineage = History.Json }] };
            Gate = new(Query, History.Transactions, History.Tenant, Options.Create(new DatabaseOptions { Provider = provider }), History.Guard);
        }
    }
    private sealed class MetadataQuery : IQueryExecutor
    {
        internal AiChatMessageRecord[] Rows = []; internal SqlStatement? Statement; internal int Calls;
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { Calls++; Statement = statement; return Task.FromResult((IReadOnlyList<T>)(object)Rows); }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
