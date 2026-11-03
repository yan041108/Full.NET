using Full.NET.Abstractions.Time;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.UnitTests.Ai;

/// <summary>冻结内容、累计私人来源和独立批准规则；这些纯规则不充当实际数据库或模型证明。</summary>
[TestClass]
public sealed class AiKnowledgeEvaluationPolicyTests
{
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static AiFeedbackEvaluationSample Sample() => new("zh-CN", "人工脱敏问题", "人工参考答案", "人工独立依据", false);
    private static AiKnowledgeEvaluationBody Body(string state = "draft") => new(1, "冻结测试集", state,
        [new(Guid.CreateVersion7(), Sample(), "独立人工编写", null)]);

    [TestMethod]
    public void Maximum_legal_chinese_cases_roundtrip_with_the_same_write_and_read_budget()
    {
        var sample = new AiFeedbackEvaluationSample("zh-CN", new('问',2048), new('答',4096), new('据',8192), true);
        var body = new AiKnowledgeEvaluationBody(1, new('集',160), "frozen", Enumerable.Range(0,8)
            .Select(_ => new AiKnowledgeEvaluationCase(Guid.CreateVersion7(),sample,new('人',1024),null)).ToArray());
        var json = AiKnowledgeEvaluationPolicy.Encode(body);
        Assert.AreEqual(json, AiKnowledgeEvaluationPolicy.Encode(AiKnowledgeEvaluationPolicy.Decode(json)));
    }

    [TestMethod]
    [DataRow("published")]
    [DataRow("approved")]
    [DataRow("processing")]
    public void Frozen_dataset_is_not_a_publication_or_execution_state(string state) =>
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Encode(Body(state)));

    [TestMethod]
    public void Duplicate_case_id_and_missing_independent_basis_are_rejected()
    {
        var original = Body(); var item = original.Cases[0];
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Encode(original with { Cases = [item,item] }));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Encode(original with { Cases = [item with { IndependentBasis = null }] }));
    }

    [TestMethod]
    public void Removing_cases_and_copying_never_removes_source_or_content_contributors()
    {
        var origin = Source(); var first = AiKnowledgeEvaluationPolicy.Extend(null,[origin],Actor);
        var copier = Guid.CreateVersion7(); var copied = AiKnowledgeEvaluationPolicy.Extend(first,[],copier);
        CollectionAssert.AreEqual(new[] {origin}, copied.Sources.ToArray());
        Assert.IsFalse(AiKnowledgeEvaluationPolicy.CanApprove(Body("frozen"),copied,Actor));
        Assert.IsFalse(AiKnowledgeEvaluationPolicy.CanApprove(Body("frozen"),copied,copier));
        Assert.IsTrue(AiKnowledgeEvaluationPolicy.CanApprove(Body("frozen"),copied,Guid.CreateVersion7()));
        Assert.IsFalse(AiKnowledgeEvaluationPolicy.CanApprove(Body(),copied,Guid.CreateVersion7()));
    }

    [TestMethod]
    public void Existing_derived_case_cannot_become_an_independent_case_by_dropping_its_footprint()
    {
        var source = Source(); var body = Body() with { Cases = [new(Guid.CreateVersion7(),Sample(),null,AiKnowledgeEvaluationPolicy.Hash(source))] };
        var empty = AiKnowledgeEvaluationPolicy.Extend(null,[],Actor);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Match(body,empty));
        AiKnowledgeEvaluationPolicy.Match(body,AiKnowledgeEvaluationPolicy.Extend(empty,[source],Actor));
    }

    [TestMethod]
    public void Different_versions_of_the_same_feedback_cannot_be_merged_as_two_sources()
    {
        var original = AiKnowledgeEvaluationPolicy.Origin(Source());
        var changed = AiKnowledgeFaqProvenanceCodec.Encode(original with { Feedback = original.Feedback! with { ReviewVersion = 2 } });
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Extend(null,
            [AiKnowledgeFaqProvenanceCodec.Encode(original),changed],Actor));
    }

    [TestMethod]
    public void Source_and_contributor_overflow_is_rejected_without_truncation()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Extend(null,Enumerable.Range(0,9).Select(_=>Source()),Actor));
        var contributors = Enumerable.Range(0,16).Select(_=>Guid.CreateVersion7()).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Extend(new(1,[],contributors),[],Actor));
    }

    [TestMethod]
    public void Canonical_json_rejects_unknown_duplicate_and_missing_protocol_fields()
    {
        var json = AiKnowledgeEvaluationPolicy.Encode(Body());
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Decode(json.Insert(1,"\"version\":1,")));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Decode(json.Insert(1,"\"approval\":true,")));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeEvaluationPolicy.Decode("{}"));
    }

    [TestMethod]
    public void Content_digest_changes_with_the_source_even_when_human_text_is_identical()
    {
        var json = AiKnowledgeEvaluationPolicy.Encode(Body());
        Assert.AreNotEqual(AiKnowledgeEvaluationPolicy.ContentHash(json,"source-one"),AiKnowledgeEvaluationPolicy.ContentHash(json,"source-two"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Persistence_candidate_binds_original_session_actor_scope_target_and_answer(bool scoped)
    {
        var clock = new Clock(); var provider = new EphemeralDataProtectionProvider();
        var tokens = new AiKnowledgeEvaluationCandidateToken(provider,clock);
        Guid? tenant = scoped ? Guid.CreateVersion7() : null;
        var session = Guid.CreateVersion7(); var target = Guid.CreateVersion7();
        var source = Source(tenant);
        var prepared = tokens.Protect(new(1,Actor,tenant,session,target,4,source,Sample(),clock.UtcNow.AddMinutes(10)));
        var read = tokens.Read(prepared.PersistenceToken,Actor,tenant,session,target);
        Assert.IsNotNull(read); Assert.AreEqual(source,read.SourceJson);
        Assert.IsNull(tokens.Read(prepared.PersistenceToken,Guid.CreateVersion7(),tenant,session,target));
        Assert.IsNull(tokens.Read(prepared.PersistenceToken,Actor,tenant,Guid.CreateVersion7(),target));
        Assert.IsNull(tokens.Read(prepared.PersistenceToken,Actor,tenant,session,Guid.CreateVersion7()));
        Assert.IsNull(tokens.Read(prepared.PersistenceToken,Actor,Guid.CreateVersion7(),session,target));
        // 旧候选目的地没有原答案摘要与目标准确版本，不能提升成持久化凭据。
        var old = provider.CreateProtector("Full.NET.Ai.FeedbackEvaluation.Candidate.v1").Protect("{}");
        Assert.IsNull(tokens.Read(old,Actor,tenant,session,target));
        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        Assert.IsNull(tokens.Read(prepared.PersistenceToken,Actor,tenant,session,target));
    }

    internal static string Source(Guid? tenant = null)
    {
        var hash = new string('A',64);
        var dependency = new AiKnowledgeAnswerDependency(Guid.CreateVersion7(),Guid.CreateVersion7(),Guid.CreateVersion7(),1,1,
            Guid.CreateVersion7(),hash,Guid.CreateVersion7(),hash,hash,hash,Guid.CreateVersion7());
        var binding = new AiKnowledgeFaqFeedbackBinding(Guid.CreateVersion7(),1,Guid.CreateVersion7(),1,Guid.CreateVersion7(),
            Guid.CreateVersion7(),Guid.CreateVersion7(),tenant,new string('a',64),hash,hash,hash);
        return AiKnowledgeFaqProvenanceCodec.Encode(new(3,"feedback_derived",null,[dependency]) { Feedback = binding });
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2026,10,30,0,0,0,TimeSpan.Zero); }
}
