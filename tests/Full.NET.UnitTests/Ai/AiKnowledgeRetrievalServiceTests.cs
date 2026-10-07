using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Full.NET.Abstractions.Ids;
using System.Text;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow("role")] [DataRow("organization")]
    public async Task Retrieval_reads_current_relationships_after_final_permission_wait(string relationship)
    {
        var r = await RetrievalFixture.Create(true); r.Actor = Guid.CreateVersion7(); var id = Guid.CreateVersion7();
        if (relationship == "role") { r.RoleIds = [id]; r.Query.RequiredRole = id; }
        else { r.OrganizationIds = [id]; r.Query.RequiredOrganization = id; }
        var snapshot = await r.Read(); r.PermissionCalls = 0;
        r.BeforeAuthorization = () => { if (r.PermissionCalls == 16) { r.RoleIds = []; r.OrganizationIds = []; } };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => r.Service.RevalidateAsync(snapshot, default));
    }
    [TestMethod]
    public async Task Retrieval_final_resource_query_follows_last_permission_wait()
    {
        var r = await RetrievalFixture.Create(false); var snapshot = await r.Read(); r.PermissionCalls = 0;
        r.BeforeAuthorization = () => { if (r.PermissionCalls == 16) r.Query.Rows = []; };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => r.Service.RevalidateAsync(snapshot, default));
    }
    [TestMethod]
    public async Task Retrieval_captured_vectors_must_match_active_receipt_even_when_current_proof_is_valid()
    {
        var r = await RetrievalFixture.Create(false); var original = r.F.Payloads[0]; var changed = original.Payload.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(changed.AsSpan(changed.Length - 4), 1);
        r.Query.Vectors = [original with { Payload = changed, PayloadHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(changed)) }, r.F.Payloads[1]];
        Assert.IsTrue(await r.F.Store.IsReadyForReadAsync(r.F.ReadRow!, new(), default));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await r.Read());
    }
    [TestMethod]
    public async Task Retrieval_freezes_query_vector_and_requested_ids_before_wait()
    {
        var r = await RetrievalFixture.Create(false); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        r.Query.BeforeVectors = async token => { entered.SetResult(); await release.Task.WaitAsync(token); };
        var ids = new[] { r.F.Binding.KnowledgeBaseId }; var vector = AiKnowledgeRetrievalRuleTests.Vector(1, 0);
        var pending = r.Service.RetrieveAsync(ids, r.F.Binding.OwnerUserId, r.Model, vector, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); ids[0] = Guid.CreateVersion7(); Array.Clear(vector); release.SetResult();
        var result = await pending; Assert.AreEqual(r.F.Binding.KnowledgeBaseId, result.KnowledgeBaseIds[0]);
        Assert.IsTrue(result.Chunks.All(c => c.Score == 1));
    }
    [TestMethod] [DataRow("permission")] [DataRow("visibility")] [DataRow("scope")] [DataRow("transaction")]
    public async Task Retrieval_rechecks_authority_after_payload_query_wait(string fault)
    {
        var r = await RetrievalFixture.Create(false);
        r.Query.BeforeVectors = _ =>
        {
            if (fault == "permission") r.Denied = true; if (fault == "visibility") r.Query.Rows = [];
            if (fault == "scope") r.F.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他"));
            if (fault == "transaction") r.F.Transactions.HasTransaction.Returns(true); return Task.CompletedTask;
        };
        if (fault is "permission" or "scope") await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(async () => await r.Read());
        else if (fault == "transaction") await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await r.Read());
        else await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await r.Read());
    }
    [TestMethod]
    public async Task Retrieval_cancellation_observes_cooperative_io_cleanup()
    {
        var r = await RetrievalFixture.Create(false); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        r.Query.BeforeVectors = async token => { entered.SetResult(); try { await Task.Delay(Timeout.Infinite, token); } finally { cleaned = true; } };
        using var stop = new CancellationTokenSource();
        var pending = r.Service.RetrieveAsync([r.F.Binding.KnowledgeBaseId], r.F.Binding.OwnerUserId, r.Model, AiKnowledgeRetrievalRuleTests.Vector(1, 0), stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending); Assert.IsTrue(cleaned);
    }
    [TestMethod] [DataRow("oversize_payload")] [DataRow("aggregate_bytes")] [DataRow("unexpected_task")] [DataRow("duplicate_batch")]
    public async Task Retrieval_refuses_invalid_actual_payload_set(string fault)
    {
        var r = await RetrievalFixture.Create(false);
        r.Query.Vectors = fault switch
        {
            "oversize_payload" => [r.F.Payloads[0] with { Payload = new byte[524289] }],
            "aggregate_bytes" => Enumerable.Repeat(r.F.Payloads[0] with { Payload = new byte[524288] }, 17).ToArray(),
            "unexpected_task" => [r.F.Payloads[0] with { IndexTaskId = Guid.CreateVersion7() }],
            _ => [r.F.Payloads[0], r.F.Payloads[0]]
        };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await r.Read());
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Answer_pipeline_returns_body_only_after_current_permission_recheck(bool revoke)
    {
        var r = await RetrievalFixture.Create(false); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var pipeline = new AiKnowledgeAnswerPipeline(r.Service, ids);
        var prepared = await pipeline.PrepareAsync([r.F.Binding.KnowledgeBaseId], r.F.Binding.OwnerUserId, r.Model,
            AiKnowledgeRetrievalRuleTests.Vector(1, 0), "问题", default);
        var id = prepared.Context.Citations[0].Id; r.Denied = revoke;
        var bytes = Encoding.UTF8.GetBytes($$"""{"answer":"答案","citationIds":["{{id}}"],"refusal":false}""");
        if (revoke) await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => pipeline.AcceptAsync(prepared, bytes, default));
        else { var answer = await pipeline.AcceptAsync(prepared, bytes, default); Assert.AreEqual("答案", answer.Text); Assert.AreEqual(1, answer.Citations.Count); }
    }
    [TestMethod]
    public async Task Answer_pipeline_keeps_uncited_source_in_full_dependency_set()
    {
        var r = await RetrievalFixture.Create(false); var snapshot = await r.Read(); var context = AiKnowledgeAnswerContextTests.Make(snapshot.Chunks[0].Chunk);
        var extra = snapshot.Sources[0] with { DocumentId = Guid.CreateVersion7(), VersionId = Guid.CreateVersion7(), TaskId = Guid.CreateVersion7() };
        var prepared = new AiKnowledgePreparedAnswer(snapshot with { Sources = [snapshot.Sources[0], extra] }, context);
        var ids = Substitute.For<IIdGenerator>(); var pipeline = new AiKnowledgeAnswerPipeline(r.Service, ids);
        var bytes = Encoding.UTF8.GetBytes($$"""{"answer":"答案","citationIds":["{{context.Citations[0].Id}}"],"refusal":false}""");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => pipeline.AcceptAsync(prepared, bytes, default));
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Retrieval_reads_actual_completed_pdf_payload_without_original_session_liveness(bool scoped)
    {
        var r = await RetrievalFixture.Create(scoped); r.F.Denied = true;
        var snapshot = await r.Read(); Assert.AreEqual(17, snapshot.Chunks.Count);
        Assert.IsTrue(snapshot.Chunks.All(c => c.Chunk.PageNumber is >= 1)); Assert.AreEqual(1, snapshot.Sources.Count);
        Assert.IsTrue(r.Query.SourceReads >= 2); Assert.AreEqual(1, r.Query.VectorReads);
    }
    [TestMethod] [DataRow("permission")] [DataRow("payload")] [DataRow("visibility")] [DataRow("model_destination")]
    [DataRow("transaction")] [DataRow("source_overflow")] [DataRow("batch_overflow")]
    public async Task Retrieval_refuses_untrusted_or_unbounded_inputs(string fault)
    {
        var r = await RetrievalFixture.Create(false);
        if (fault == "permission") r.Denied = true;
        if (fault == "payload") r.F.Payloads[0].Payload[0] ^= 1;
        if (fault == "visibility") r.Query.Rows = [];
        if (fault == "model_destination") r.Query.Model = new() { Id = r.Model.Id, Version = 1, ProviderKey = r.Model.ProviderKey, ModelId = r.Model.ModelId, IsEnabled = true, EndpointBaseUrl = "https://other.example/v1" };
        if (fault == "transaction") r.F.Transactions.HasTransaction.Returns(true);
        if (fault == "source_overflow") r.Query.Rows = Enumerable.Repeat(r.F.ReadRow!, 9).ToArray();
        if (fault == "batch_overflow") r.Query.Vectors = Enumerable.Repeat(r.F.Payloads[0], 33).ToArray();
        if (fault == "transaction") await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await r.Read());
        else if (fault == "permission") await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(async () => await r.Read());
        else await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await r.Read());
    }
    [TestMethod] [DataRow("permission")] [DataRow("visibility")] [DataRow("scope")] [DataRow("model_destination")] [DataRow("payload")]
    public async Task Retrieval_revalidates_all_context_dependencies_after_wait(string fault)
    {
        var r = await RetrievalFixture.Create(false); var snapshot = await r.Read();
        if (fault == "permission") r.Denied = true;
        if (fault == "visibility") r.Query.Rows = [];
        if (fault == "scope") r.F.Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "其他"));
        if (fault == "model_destination") r.Query.Model = new() { Id = r.Model.Id, Version = 1, ProviderKey = r.Model.ProviderKey, ModelId = r.Model.ModelId, IsEnabled = true, EndpointBaseUrl = "https://other.example/v1" };
        if (fault == "payload") r.F.Payloads[0].Payload[0] ^= 1;
        if (fault is "permission" or "scope") await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => r.Service.RevalidateAsync(snapshot, default));
        else await Assert.ThrowsExactlyAsync<InvalidDataException>(() => r.Service.RevalidateAsync(snapshot, default));
    }
    private sealed class RetrievalFixture
    {
        internal Fixture F = null!; internal RetrievalQuery Query = null!;
        internal AiKnowledgeRetrievalService Service = null!; internal AiModelConfigRecord Model = null!;
        internal bool Denied; internal int PermissionCalls; internal Action? BeforeAuthorization; internal Guid Actor;
        internal Guid[] RoleIds = [], OrganizationIds = [];
        internal static async Task<RetrievalFixture> Create(bool scoped)
        {
            var r = new RetrievalFixture { F = new(scoped) }; var f = r.F; r.Actor = f.Binding.OwnerUserId; Assert.IsTrue(await f.Activate()); f.ReadRow = ReadRow(f);
            r.Model = await f.Query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(AiModelConfigSql.FindAvailableForHostChat, new(), default) ?? throw new InvalidOperationException();
            r.Query = new(f.Query) { Rows = [f.ReadRow], Vectors = f.Payloads, Model = r.Model };
            var auth = Substitute.For<ICurrentSessionAuthorization>();
            auth.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
            { Assert.IsFalse(f.Transactions.HasTransaction); r.PermissionCalls++; r.BeforeAuthorization?.Invoke(); return r.Denied ? null : new AuthorizedSessionActor(r.Actor, f.Tenant.Id, Guid.CreateVersion7()); });
            var roles = Substitute.For<IActiveRoleDirectory>(); var orgs = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
            roles.ListActiveRoleIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(f.Transactions.HasTransaction); return r.RoleIds; });
            orgs.ListActiveUnitIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(f.Transactions.HasTransaction); return r.OrganizationIds; });
            r.Service = new(r.Query, f.Transactions, f.Tenant, new(roles, orgs, f.Tenant),
                new(auth, Substitute.For<IHttpContextAccessor>(), Substitute.For<ICurrentApiKeyAuthorization>()), null!, f.Store,
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
            return r;
        }
        internal Task<AiKnowledgeRetrievalSnapshot> Read() => Service.RetrieveAsync([F.Binding.KnowledgeBaseId], Actor, Model, AiKnowledgeRetrievalRuleTests.Vector(1, 0), default);
    }
    private sealed class RetrievalQuery(IQueryExecutor inner) : IQueryExecutor
    {
        internal AiKnowledgeIndexStatusRecord[] Rows = []; internal AiKnowledgeIndexVectorRecord[] Vectors = [];
        internal AiModelConfigRecord Model = null!; internal int SourceReads, VectorReads;
        internal Func<CancellationToken, Task>? BeforeVectors;
        internal Guid? RequiredRole, RequiredOrganization;
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement sql, object? p = null, CancellationToken token = default) =>
            typeof(T) == typeof(AiModelConfigRecord) ? Task.FromResult((T?)(object)Model) : inner.QuerySingleOrDefaultAsync<T>(sql, p, token);
        public async Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement sql, object? p = null, CancellationToken token = default)
        {
            if (typeof(T) == typeof(AiKnowledgeIndexStatusRecord))
            {
                SourceReads++; var parameters = (Dictionary<string, object?>)p!;
                var visible = (!RequiredRole.HasValue || ((string)parameters["ActiveRoleIdsJson"]!).Contains(RequiredRole.Value.ToString(), StringComparison.Ordinal))
                    && (!RequiredOrganization.HasValue || ((string)parameters["ActiveOrganizationUnitIdsJson"]!).Contains(RequiredOrganization.Value.ToString(), StringComparison.Ordinal));
                return (IReadOnlyList<T>)(object)(visible ? Rows : Array.Empty<AiKnowledgeIndexStatusRecord>());
            }
            if (typeof(T) == typeof(AiKnowledgeIndexVectorRecord)) { VectorReads++; if (BeforeVectors is not null) await BeforeVectors(token); return (IReadOnlyList<T>)(object)Vectors; }
            return await inner.QueryAsync<T>(sql, p, token);
        }
    }
}
