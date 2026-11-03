using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.UnitTests.Ai;

/// <summary>FAQ业务来源闭合版本、不可降级、完整预算与所属人一致性。</summary>
[TestClass]
public sealed class AiKnowledgeFaqBusinessSourceTests
{
    [TestMethod]
    [DataRow(Full.NET.Data.Abstractions.DatabaseProvider.SqlServer, false)]
    [DataRow(Full.NET.Data.Abstractions.DatabaseProvider.SqlServer, true)]
    [DataRow(Full.NET.Data.Abstractions.DatabaseProvider.MySql, false)]
    [DataRow(Full.NET.Data.Abstractions.DatabaseProvider.MySql, true)]
    public void Final_stored_binding_keeps_owner_private_classification_and_root_acl(Full.NET.Data.Abstractions.DatabaseProvider provider, bool scoped)
    {
        foreach (var statement in new[] { AiKnowledgeFaqDraftSql.CurrentFor(provider, scoped),
            AiKnowledgeFaqDraftSql.SharedCurrentFor(provider, scoped), AiKnowledgeFaqDraftSql.ExportCurrentFor(provider, scoped) })
        {
            StringAssert.Contains(statement.Text, "$.personalSchedules[0].ownerUserId");
            StringAssert.Contains(statement.Text, "=@OwnerUserId");
            StringAssert.Contains(statement.Text, "k.IsEnabled=1");
            StringAssert.Contains(statement.Text, provider == Full.NET.Data.Abstractions.DatabaseProvider.MySql
                ? "BINARY k.DataClassification=BINARY 'restricted'" : "CONVERT(varbinary(max),N'restricted')");
            Assert.AreEqual(scoped ? Full.NET.Data.Abstractions.SqlDataScope.TenantRequired : Full.NET.Data.Abstractions.SqlDataScope.HostOnly, statement.Scope);
        }
    }

    private static readonly Guid Actor = Guid.CreateVersion7();
    private static PersonalScheduleKnowledgeProof Proof() => new(PersonalScheduleKnowledgeSourceKeys.Kind, PersonalScheduleKnowledgeSourceKeys.Purpose,
        Guid.CreateVersion7(), null, Actor, 1, new('A', 64));
    private static AiKnowledgeAnswerDependency Leaf() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, 1,
        Guid.CreateVersion7(), new('A',64), Guid.CreateVersion7(), new('B',64), new('C',64), new('D',64), Guid.CreateVersion7());
    private static AiKnowledgeFaqProvenance Origin() => new(4, "document_derived", null, [Leaf()]) { PersonalSchedules = [Proof()] };
    [TestMethod]
    public void Immutable_business_origin_roundtrips_without_body_or_subject_override()
    {
        var origin = Origin(); var json = AiKnowledgeFaqProvenanceCodec.Encode(origin);
        var row = new AiKnowledgeFaqDraftRecord { CreatedById = Actor, OriginKey = origin.Origin, SourceJson = json, SourceHash = AiKnowledgeFaqDraftPolicy.Hash(json) };
        var value = AiKnowledgeFaqProvenanceCodec.Decode(row); Assert.AreEqual(origin.PersonalSchedules![0], value.PersonalSchedules![0]);
        Assert.AreEqual(4, value.Version);
        var publicValue = AiKnowledgeFaqBusinessSources.Public(value)![0]; Assert.AreEqual(origin.PersonalSchedules[0].Id, publicValue.Id);
        Assert.IsFalse(publicValue.ToString().Contains(Actor.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Decode(row with { CreatedById = Guid.CreateVersion7() }));
    }
    [TestMethod] [DataRow("old")] [DataRow("missing")] [DataRow("empty")] [DataRow("independent")] [DataRow("owner")] [DataRow("conflict")] [DataRow("budget")]
    public void Invalid_or_downgraded_business_origin_is_rejected(string fault)
    {
        var origin = Origin(); var proof = origin.PersonalSchedules![0];
        origin = fault switch { "old" => origin with { Version = 1 }, "missing" => origin with { PersonalSchedules = null },
            "empty" => origin with { PersonalSchedules = [] }, "independent" => origin with { Origin = "independent", Basis = "手工依据", Dependencies = [] },
            "owner" => origin with { PersonalSchedules = [proof, proof with { Id = Guid.CreateVersion7(), OwnerUserId = Guid.CreateVersion7() }] },
            "conflict" => origin with { PersonalSchedules = [proof, proof with { Version = 2 }] },
            _ => origin with { Dependencies = Enumerable.Range(0,8).Select(_ => Leaf()).ToArray() } };
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqProvenanceCodec.Encode(origin));
    }
    [TestMethod]
    public void Identical_full_proofs_merge_but_stale_same_id_is_not_deduplicated()
    {
        var proof = Proof(); Assert.AreEqual(1, AiKnowledgeFaqBusinessSources.Freeze([proof, proof])!.Count);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeFaqBusinessSources.Freeze([proof, proof with { Version = 2 }]));
    }
    [TestMethod]
    public void Inherited_business_faq_requires_v8_and_complete_top_level_proof()
    {
        var origin = Origin(); var proof = origin.PersonalSchedules![0];
        var faq = new AiKnowledgeFaqAnswerSource(Guid.CreateVersion7(), Guid.CreateVersion7(), new('A',64), new('B',64), Guid.CreateVersion7(), 1, origin.Dependencies)
            { PersonalSchedules = origin.PersonalSchedules };
        var dependency = Leaf() with { FileId = null, Faq = faq };
        var lineage = new AiKnowledgeAnswerLineage(8, Guid.CreateVersion7(), Guid.CreateVersion7(), Actor, null, [dependency], [])
            { PersonalSchedules = [proof], PersonalScheduleReferences = [] };
        var encoded = AiKnowledgeAnswerLineageCodec.Encode(lineage);
        var decoded = AiKnowledgeAnswerLineageCodec.Decode(encoded,lineage.MessageId,lineage.SessionId,Actor,null);
        Assert.AreEqual(proof,decoded.Dependencies[0].Faq!.PersonalSchedules![0]);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Encode(lineage with { Version = 7, PersonalSchedules = null, PersonalScheduleReferences = null }));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeAnswerLineageCodec.Encode(lineage with { PersonalSchedules = [proof with { Version = 2 }] }));
    }
    [TestMethod] [DataRow("public",false)] [DataRow("internal",false)] [DataRow("restricted",true)] [DataRow("unknown",false)]
    public void Business_root_classification_cannot_be_lowered_even_if_all_document_leaves_are_public(string classification,bool expected)
    {
        var origin=Origin();var id=Guid.CreateVersion7();var leaf=origin.Dependencies[0];
        var result=AiKnowledgeFaqClassification.ForParent(id,origin,[new(){Id=id,IsEnabled=true,DataClassification=classification},
            new(){Id=leaf.KnowledgeBaseId,IsEnabled=true,DataClassification="public"}]);
        Assert.AreEqual(expected,result is not null);if(result is not null)Assert.AreEqual("restricted",result.Value);
    }
    [TestMethod]
    public void Real_parent_capture_keeps_ancestor_business_proof_without_injecting_it_as_a_document()
    {
        var proof=Proof();var first=AiKnowledgeFaqGraphTests.Node();var second=AiKnowledgeFaqGraphTests.Node(first.Status.DocumentId);
        first=first with {Status=first.Status with {OwnerUserId=Actor,DataClassification="restricted"},Source=first.Source with {PersonalSchedules=[proof]}};
        second=second with {Status=second.Status with {OwnerUserId=Actor,DataClassification="restricted"},Source=second.Source with {PersonalSchedules=[proof]}};
        var ancestor=new AiKnowledgeFaqGraph(1,[first.Status.DocumentId],[first]);
        var captured=AiKnowledgeFaqGraphs.Capture([second.Status],new Dictionary<Guid,AiKnowledgeFaqAnswerSource>
            {[second.Status.DocumentId]=second.Source with {Graph=ancestor,CurrentPublication=second.Publication}});
        Assert.AreEqual(4,captured.Version);Assert.AreEqual(proof,captured.PersonalSchedules!.Single());Assert.AreEqual(0,captured.Dependencies.Count);
        Assert.AreEqual(2,captured.Graph!.Nodes.Count);Assert.IsFalse(AiKnowledgeFaqGraphs.Public(captured).Any(value=>value.DocumentId==proof.Id));
        _=AiKnowledgeFaqProvenanceCodec.Encode(captured);
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFaqProvenanceCodec.Encode(captured with {Version=2,PersonalSchedules=null}));
    }
    [TestMethod]
    public void Child_cannot_drop_business_proofs_retained_by_a_real_parent_edge()
    {
        var proof=Proof();var first=AiKnowledgeFaqGraphTests.Node();var second=AiKnowledgeFaqGraphTests.Node(first.Status.DocumentId);
        first=first with {Source=first.Source with {PersonalSchedules=[proof]}};
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFaqGraphs.Validate(new(1,[second.Status.DocumentId],[first,second]),[]));
    }
}
