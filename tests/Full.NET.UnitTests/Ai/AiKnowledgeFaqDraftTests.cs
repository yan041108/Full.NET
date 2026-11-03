using System.Text.Json;
using System.Data;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.UnitTests.Ai;

/// <summary>草稿规范化、原始来源不可降级及完整当前来源证明的定向边界。</summary>
[TestClass]
public sealed class AiKnowledgeFaqDraftPolicyTests
{
    [TestMethod] [DataRow(false,false)] [DataRow(true,false)] [DataRow(false,true)] [DataRow(true,true)]
    public void Faq_draft_static_reader_preserves_complete_genesis_and_nullable_identity(bool mysql, bool absent)
    {
        using var table = new DataTable(); var id = Guid.CreateVersion7(); var now = new DateTime(2026,10,29,3,0,0,DateTimeKind.Utc);
        foreach (var name in new[] { "RestoredFromRevisionId","RequestKey","RevisionId","HeadRevisionId","CreateRequestKey","CreatedById","TenantId","ReservedDocumentId","KnowledgeBaseId","Id" }) table.Columns.Add(name,typeof(Guid));
        foreach (var name in new[] { "CreateHash","OriginKey","Basis","SourceJson","SourceHash","RequestHash","ContentJson","ContentHash" }) table.Columns.Add(name,typeof(string));
        foreach (var name in new[] { "HeadVersion","RevisionNo" }) table.Columns.Add(name,mysql?typeof(long):typeof(int));
        foreach (var name in new[] { "CreatedAtUtc","RevisionCreatedAtUtc" }) table.Columns.Add(name,mysql?typeof(DateTime):typeof(DateTimeOffset));
        var values=table.NewRow(); foreach(DataColumn column in table.Columns) values[column]=column.DataType==typeof(Guid)?id:column.DataType==typeof(string)?"原值":column.DataType==typeof(DateTime)?now:column.DataType==typeof(DateTimeOffset)?new DateTimeOffset(now):7;
        values["OriginKey"]=absent?"independent":"document_derived";values["SourceJson"]="{\"version\":1}";values["ContentJson"]="{\"question\":\"原问题\"}";
        values["TenantId"]=absent?DBNull.Value:id;values["RestoredFromRevisionId"]=absent?DBNull.Value:id;values["Basis"]=absent?"独立依据":DBNull.Value;
        table.Rows.Add(values);using var reader=table.CreateDataReader();Assert.IsTrue(reader.Read());var row=AiBudgetRowReaders.ReadKnowledgeFaqDraft(reader);
        Assert.AreEqual(id,row.Id);Assert.AreEqual(id,row.ReservedDocumentId);Assert.AreEqual(id,row.HeadRevisionId);Assert.AreEqual(id,row.RevisionId);Assert.AreEqual(7,row.HeadVersion);Assert.AreEqual(7,row.RevisionNo);
        Assert.AreEqual(absent?null:(Guid?)id,row.TenantId);Assert.AreEqual(absent?null:(Guid?)id,row.RestoredFromRevisionId);Assert.AreEqual(absent?"独立依据":null,row.Basis);
        Assert.AreEqual("{\"version\":1}",row.SourceJson);Assert.AreEqual("{\"question\":\"原问题\"}",row.ContentJson);Assert.AreEqual(new DateTimeOffset(now),row.CreatedAtUtc);Assert.AreEqual(row.CreatedAtUtc,row.RevisionCreatedAtUtc);
    }
    internal static AiKnowledgeFaqContent Content() => new("  标准问题  ", ["同义问法"], "明确答案\n保留换行", "制度", null, null);
    [TestMethod]
    public void Faq_draft_maximum_unicode_fields_roundtrip_without_committing_unreadable_content()
    {
        var content = new AiKnowledgeFaqContent(new string('问',512), Enumerable.Range(0,16).Select(i=>new string('别',511)+(char)('A'+i)).ToArray(), new string('答',8000), new string('类',64), null, null);
        var json = AiKnowledgeFaqDraftPolicy.Encode(content); Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(json)>65536);
        var restored = AiKnowledgeFaqDraftPolicy.Decode(json); Assert.AreEqual(content.Answer,restored.Answer); CollectionAssert.AreEqual(content.Aliases.ToArray(),restored.Aliases.ToArray());
    }
    [TestMethod]
    public void Faq_draft_canonicalizes_aliases_without_changing_display_or_inventing_publication()
    {
        var value = AiKnowledgeFaqDraftPolicy.Freeze(Content()); Assert.AreEqual("标准问题", value.Question); Assert.AreEqual("明确答案\n保留换行", value.Answer);
        Assert.AreEqual("A B", AiKnowledgeFaqDraftPolicy.QuestionKey("Ａ　b"));
        var json = AiKnowledgeFaqDraftPolicy.Encode(value); Assert.IsTrue(json.Contains("\"question\"", StringComparison.Ordinal));
        Assert.AreEqual(value.Question, AiKnowledgeFaqDraftPolicy.Decode(json).Question); Assert.AreEqual(64, AiKnowledgeFaqDraftPolicy.ContentHash(json, new string('A', 64)).Length);
    }
    [TestMethod] [DataRow("question")] [DataRow("answer")] [DataRow("category")] [DataRow("count")]
    [DataRow("duplicate")] [DataRow("aliasQuestion")] [DataRow("control")] [DataRow("surrogate")] [DataRow("date")]
    public void Faq_draft_rejects_invalid_content_instead_of_truncating_or_deduplicating(string fault)
    {
        var value = Content();
        value = fault switch { "question" => value with { Question = new string('字', 513) }, "answer" => value with { Answer = new string('字', 8001) },
            "category" => value with { Category = " " }, "count" => value with { Aliases = Enumerable.Range(0,17).Select(i => "问法" + i).ToArray() },
            "duplicate" => value with { Aliases = ["A", "Ａ"] }, "aliasQuestion" => value with { Aliases = ["标准问题"] },
            "control" => value with { Answer = "答案\0" }, "surrogate" => value with { Question = new string((char)0xd800, 1) },
            _ => value with { ValidFromUtc = DateTimeOffset.UtcNow, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) } };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqDraftPolicy.Freeze(value));
    }
    [TestMethod]
    public void Faq_draft_time_uses_utc_microseconds_and_common_database_lower_bound()
    {
        var instant = new DateTimeOffset(2026,10,29,9,0,0,TimeSpan.FromHours(8)).AddTicks(1234567);
        var value = AiKnowledgeFaqDraftPolicy.Freeze(Content() with { ValidFromUtc = instant });
        Assert.AreEqual(TimeSpan.Zero, value.ValidFromUtc!.Value.Offset); Assert.AreEqual(0, value.ValidFromUtc.Value.Ticks % 10);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqDraftPolicy.Freeze(Content() with { ValidFromUtc = DateTimeOffset.MinValue }));
    }
    [TestMethod] [DataRow("extra")] [DataRow("duplicate")] [DataRow("unknown")]
    public void Faq_draft_json_rejects_extended_or_ambiguous_protocol(string fault)
    {
        var json = AiKnowledgeFaqDraftPolicy.Encode(Content());
        json = fault == "extra" ? json[..^1] + ",\"origin\":\"independent\"}" : fault == "duplicate" ? json[..^1] + ",\"question\":\"重写\"}" : "{}";
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqDraftPolicy.Decode(json));
    }
    [TestMethod] [DataRow("unknown")] [DataRow("missing")] [DataRow("basis")]
    public void Faq_draft_unknown_or_incomplete_source_never_becomes_independent(string fault) =>
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Encode(fault switch
        { "unknown" => new(2, "business", null, []), "missing" => new(1,"document_derived",null,[]), _ => new(1,"independent",null,[]) }));
    [TestMethod]
    public void Faq_draft_source_hash_binds_original_kind_and_basis()
    {
        var json = AiKnowledgeFaqProvenanceCodec.Encode(new(1,"independent","独立编写依据",[]));
        var row = new AiKnowledgeFaqDraftRecord { SourceJson = json, SourceHash = AiKnowledgeFaqDraftPolicy.Hash(json), OriginKey = "independent", Basis = "独立编写依据" };
        Assert.AreEqual("independent", AiKnowledgeFaqProvenanceCodec.Decode(row).Origin);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Decode(row with { OriginKey = "document_derived" }));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Decode(row with { Basis = "清空原始依据" }));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Decode(row with { SourceJson = json.Replace("独立", "另一个", StringComparison.Ordinal) + " " }));
    }
}

public sealed partial class AiKnowledgeIndexPdfActivationTests
{
    [TestMethod] [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public async Task Faq_business_proof_is_current_after_final_sql_without_repeating_it_inside_one_binding(bool body, bool revoked)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false);
        var root = FaqRoot(f); var origin = AiKnowledgeFaqProvenanceCodec.Decode(root);
        var business = new Full.NET.Modules.Calendar.Contracts.PersonalScheduleKnowledgeProof(
            Full.NET.Modules.Calendar.Contracts.PersonalScheduleKnowledgeSourceKeys.Kind,
            Full.NET.Modules.Calendar.Contracts.PersonalScheduleKnowledgeSourceKeys.Purpose,
            Guid.CreateVersion7(), null, f.R.Actor, 1, new('A', 64));
        var json = AiKnowledgeFaqProvenanceCodec.Encode(origin with { Version = 4, PersonalSchedules = [business] });
        root = root with { SourceJson = json, SourceHash = AiKnowledgeFaqDraftPolicy.Hash(json) };
        var query = FaqQuery(f, root); var current = true; var calls = 0;
        query.OnCurrent = () => current = !revoked;
        var port = Substitute.For<Full.NET.Modules.Calendar.Contracts.IPersonalScheduleKnowledgeSource>();
        port.VerifyBackgroundAsync(Arg.Any<SessionBindingSnapshot>(),
            Arg.Any<IReadOnlyCollection<Full.NET.Modules.Calendar.Contracts.PersonalScheduleKnowledgeProof>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls++; return current; });
        var session = new SessionBindingSnapshot(f.R.Actor, null, Guid.CreateVersion7(), "stamp", "host", "host", SessionBindingKinds.Refresh);
        var authorization = Substitute.For<IBackgroundSessionAuthorization>();
        authorization.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(session.UserId, null, session.SessionId));
        var guard = new AiKnowledgeFaqDraftSourceGuard(query, f.R.F.Transactions, f.R.F.Tenant, Options.Create(new DatabaseOptions()),
            FaqAccess(f), new FaqDraftProof(() => true), authorization,
            personalSchedules: new AiPersonalScheduleSourceReader(port, f.R.F.Transactions, f.R.F.Tenant));
        var result = body ? await guard.AuthorizeAsync([root], f.R.Actor, AiKnowledgeFaqPermissions.Index, default, original: session)
            : await guard.AuthorizeIndexBindingsAsync(root, session, default);
        Assert.AreEqual(!revoked, result.Contains(root.Id));
        Assert.AreEqual(body ? 2 : 1, calls, "同一门禁不重复核证业务来源；每次调用仍在最后 SQL 等待后读取当前权威。");
        Assert.AreEqual(0, f.Budget.Requests.Count);
    }

    [TestMethod]
    public async Task Faq_draft_database_deadlock_is_standard_conflict_without_retry()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var query = new FaqDraftDeadlockQuery();
        var service = new Full.NET.Modules.Ai.Features.ManageKnowledgeFaqs.AiKnowledgeFaqDraftService(query, null!, null!, f.R.F.Transactions, f.R.F.Tenant, Options.Create(new DatabaseOptions()), null!, null!, f.R.Permissions, null!, null!, null!, null!, null!);
        var result = await service.IndependentAsync(f.R.F.Binding.KnowledgeBaseId, f.R.Actor,
            new(Guid.CreateVersion7(), new("问题", [], "答案", "制度", null, null), "独立编写依据"), default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual("ai.knowledge_faqs.version_conflict", result.Error!.Code); Assert.AreEqual(1, query.Calls);
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.ChatCalls);
    }
    private sealed class FaqDraftDeadlockQuery : IQueryExecutor
    {
        public int Calls { get; private set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        { ++Calls; throw new DataCommandException(DataCommandFailureKind.Deadlock, new InvalidOperationException("受控死锁。")); }
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Faq_draft_source_checks_all_documents_without_chat_or_model_dispatch(bool scoped)
    {
        using var f = await PaidAnswerFixture.CreateAsync(scoped); var root = FaqRoot(f);
        var query = FaqQuery(f, root); var proof = new FaqDraftProof(() => true);
        f.R.DeniedPermission = AiChatPermissions.Read;
        var guard = new AiKnowledgeFaqDraftSourceGuard(query, f.R.F.Transactions, f.R.F.Tenant, Options.Create(new DatabaseOptions()), FaqAccess(f), proof, permissions: f.R.Permissions);
        Assert.IsTrue((await guard.AuthorizeAsync([root], f.R.Actor, AiKnowledgeFaqPermissions.Write, default)).Contains(root.Id));
        Assert.AreEqual(1, proof.Calls);
        Assert.AreEqual(0, f.Budget.Requests.Count); Assert.AreEqual(0, f.Factory.ChatCalls); Assert.AreEqual(0, f.Factory.EmbeddingCalls);
    }
    [TestMethod] [DataRow("proof")] [DataRow("publication")] [DataRow("permission")] [DataRow("faq")] [DataRow("root")]
    public async Task Faq_draft_late_withdrawal_never_releases_body_or_partial_source(string fault)
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var root = FaqRoot(f); var query = FaqQuery(f, root);
        var proof = new FaqDraftProof(() =>
        {
            if (fault == "publication") f.R.Query.Rows = [f.R.Query.Rows[0] with { PublicationId = Guid.CreateVersion7() }];
            if (fault == "permission") f.R.DeniedPermission = AiKnowledgeDocumentPermissions.IndexRead;
            if (fault == "faq") f.R.DeniedPermission = AiKnowledgeFaqPermissions.Read;
            if (fault == "root") query.RootVisible = false;
            return fault != "proof";
        });
        var guard = new AiKnowledgeFaqDraftSourceGuard(query, f.R.F.Transactions, f.R.F.Tenant, Options.Create(new DatabaseOptions()), FaqAccess(f), proof, permissions: f.R.Permissions);
        Assert.AreEqual(0, (await guard.AuthorizeAsync([root], f.R.Actor, AiKnowledgeFaqPermissions.Read, default)).Count);
        Assert.AreEqual(0, f.Budget.Requests.Count);
    }
    [TestMethod]
    public async Task Faq_draft_unknown_nested_faq_document_is_rejected_before_payload_proof()
    {
        using var f = await PaidAnswerFixture.CreateAsync(false); var root = FaqRoot(f); var query = FaqQuery(f, root);
        query.BoundDocuments = 1;
        var proof = new FaqDraftProof(() => true);
        var guard = new AiKnowledgeFaqDraftSourceGuard(query, f.R.F.Transactions, f.R.F.Tenant, Options.Create(new DatabaseOptions()), FaqAccess(f), proof, permissions: f.R.Permissions);
        Assert.AreEqual(0, (await guard.AuthorizeAsync([root], f.R.Actor, AiKnowledgeFaqPermissions.Write, default)).Count);
        Assert.AreEqual(0, proof.Calls);
    }
    private static AiKnowledgeFaqDraftRecord FaqRoot(PaidAnswerFixture f)
    {
        var json = AiKnowledgeFaqProvenanceCodec.Encode(new(1,"document_derived",null,[AiKnowledgeFaqProvenanceCodec.Dependency(f.R.Query.Rows[0])]));
        return new() { Id = Guid.CreateVersion7(), KnowledgeBaseId = f.R.F.Binding.KnowledgeBaseId, TenantId = f.R.F.Tenant.Id, SourceJson = json,
            SourceHash = AiKnowledgeFaqDraftPolicy.Hash(json), OriginKey = "document_derived", HeadRevisionId = Guid.CreateVersion7(), HeadVersion = 1 };
    }
    private static AiKnowledgeReadAccess FaqAccess(PaidAnswerFixture f)
    {
        var roles = Substitute.For<IActiveRoleDirectory>(); var organizations = Substitute.For<ICurrentTenantOrganizationUnitDirectory>();
        roles.ListActiveRoleIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        organizations.ListActiveUnitIdsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Guid>());
        return new(roles, organizations, f.R.F.Tenant);
    }
    private static FaqDraftQuery FaqQuery(PaidAnswerFixture f, AiKnowledgeFaqDraftRecord root) => new(f, root);
    private sealed class FaqDraftProof(Func<bool> verify) : IAiKnowledgeAnswerSourceProof
    {
        public int Calls { get; private set; }
        public Task<bool> VerifyAsync(AiKnowledgeIndexStatusRecord source, Dictionary<string, object?> parameters, CancellationToken token) { ++Calls; return Task.FromResult(verify()); }
    }
    private sealed class FaqDraftQuery(PaidAnswerFixture fixture, AiKnowledgeFaqDraftRecord root) : IQueryExecutor
    {
        public bool RootVisible { get; set; } = true;
        public int BoundDocuments { get; set; }
        public Action? OnCurrent { get; set; }
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(typeof(T) == typeof(AiKnowledgeBaseRecord)
                ? (T?)(object)new AiKnowledgeBaseRecord { Id = root.KnowledgeBaseId, IsEnabled = true, DataClassification = "restricted" }
                : (T?)(object)BoundDocuments);
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(AiKnowledgeIndexStatusRecord)) return Task.FromResult<IReadOnlyList<T>>(fixture.R.Query.Rows.Cast<T>().ToArray());
            if (typeof(T) == typeof(Guid))
            {
                OnCurrent?.Invoke();
                // 当前绑定 SQL 同时核对叶发布；夹具不能只检查根可见而忽略已替换的来源。
                var dependencies=AiKnowledgeFaqProvenanceCodec.Decode(root).Dependencies;
                var current=RootVisible&&dependencies.All(dependency=>fixture.R.Query.Rows.Any(row=>AiKnowledgeAnswerLineageCodec.Matches(dependency,row)));
                return Task.FromResult<IReadOnlyList<T>>(current ? [(T)(object)root.Id] : []);
            }
            throw new InvalidOperationException("未登记的 FAQ 定向查询。");
        }
    }
}
