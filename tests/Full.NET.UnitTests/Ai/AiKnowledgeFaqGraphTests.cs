using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Data.Abstractions;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实父资格不能展平成文档叶；平面图拒绝环、孤点、截断和超限，且来源协议保持封闭。</summary>
[TestClass]
public sealed class AiKnowledgeFaqGraphTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    internal static AiKnowledgeFaqParentNode Node(params Guid[] parents)
    {
        var revision = Guid.CreateVersion7(); var model = Guid.CreateVersion7();
        var status = new AiKnowledgeIndexStatusRecord { KnowledgeBaseId=Guid.CreateVersion7(),DocumentId=Guid.CreateVersion7(),VersionId=Guid.CreateVersion7(),PublicationId=Guid.CreateVersion7(),
            DocumentVersion=1,KnowledgeBaseVersion=1,OwnerUserId=Guid.CreateVersion7(),FaqRevisionId=revision,ContentHash=Hash,ContentType="text/plain",ParsingPolicyKey="faq-revision-text-v1",DataClassification="internal",
            TaskId=Guid.CreateVersion7(),ActiveLeaseToken=Guid.CreateVersion7(),ActiveReceiptHash=Hash,ActiveManifestHash=Hash,ActivePlanHash=Hash,StateKey="completed",IsCurrent=1,HasDerivedData=1,ModelConfigId=model,ModelConfigVersion=1 };
        status=status with { ActiveTaskId=status.TaskId };
        return new(status,new(Guid.CreateVersion7(),revision,Hash,Hash,model,1,[]),parents,new(status.PublicationId!.Value,3,Hash));
    }
    [TestMethod]
    public void Parent_graph_roundtrips_real_nodes_and_edges_without_fabricating_document_leaves()
    {
        var first=Node();var second=Node(first.Status.DocumentId);var graph=new AiKnowledgeFaqGraph(1,[second.Status.DocumentId],[first,second]);
        var origin=new AiKnowledgeFaqProvenance(2,"faq_derived",null,[]){Graph=graph};
        var json=AiKnowledgeFaqProvenanceCodec.Encode(origin);
        var restored=AiKnowledgeFaqProvenanceCodec.Decode(new(){Id=Guid.CreateVersion7(),ReservedDocumentId=Guid.CreateVersion7(),OriginKey="faq_derived",SourceJson=json,SourceHash=AiKnowledgeFaqDraftPolicy.Hash(json)});
        Assert.IsTrue(AiKnowledgeFaqGraphs.Same(graph,restored.Graph));Assert.AreEqual(0,restored.Dependencies.Count);
        Assert.AreEqual(2,AiKnowledgeFaqGraphs.Public(restored).Count);Assert.IsTrue(AiKnowledgeFaqGraphs.Public(restored).All(s=>s.SourceKind=="faq-revision-v1"));
        Assert.AreEqual(first.Status.DocumentId,AiKnowledgeFaqGraphs.Ancestors(graph,second)!.Roots.Single());
    }
    [TestMethod]
    [DataRow("cycle")] [DataRow("truncated")] [DataRow("orphan")] [DataRow("duplicate")] [DataRow("roots")] [DataRow("version")]
    [DataRow("file")] [DataRow("lease")] [DataRow("model")] [DataRow("source")] [DataRow("self_faq")] [DataRow("self_document")] [DataRow("publication")]
    public void Parent_graph_rejects_missing_or_forged_qualification(string fault)
    {
        var first=Node();var second=Node(first.Status.DocumentId);var graph=new AiKnowledgeFaqGraph(1,[second.Status.DocumentId],[first,second]);
        graph=fault switch {"cycle"=>graph with{Nodes=[first with{ParentDocumentIds=[second.Status.DocumentId]},second]},"truncated"=>graph with{Nodes=[second]},
            "orphan"=>graph with{Nodes=[first,second,Node()]},"duplicate"=>graph with{Nodes=[first,first]},"roots"=>graph with{Roots=[]},"version"=>graph with{Version=99},
            "file"=>graph with{Nodes=[first with{Status=first.Status with{FileId=Guid.CreateVersion7()}},second]},"lease"=>graph with{Nodes=[first with{Status=first.Status with{ActiveLeaseToken=null}},second]},
            "model"=>graph with{Nodes=[first with{Source=first.Source with{ModelConfigVersion=0}},second]},"source"=>graph with{Nodes=[first with{Source=first.Source with{RevisionId=Guid.CreateVersion7()}},second]},
            "publication"=>graph with{Nodes=[first with{Publication=first.Publication with{Id=Guid.CreateVersion7()}},second]},_=>graph};
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFaqGraphs.Validate(graph,[],fault=="self_faq"?first.Source.FaqId:null,fault=="self_document"?first.Status.DocumentId:null));
    }
    [TestMethod]
    public void Parent_graph_preserves_all_four_levels_and_rejects_a_fifth_instead_of_truncation()
    {
        var nodes=new List<AiKnowledgeFaqParentNode>();for(var i=0;i<4;i++)nodes.Add(Node(i==0?[]:[nodes[^1].Status.DocumentId]));
        AiKnowledgeFaqGraphs.Validate(new(1,[nodes[^1].Status.DocumentId],nodes),[]);
        nodes.Add(Node(nodes[^1].Status.DocumentId));Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFaqGraphs.Validate(new(1,[nodes[^1].Status.DocumentId],nodes),[]));
    }
    [TestMethod]
    public void Persisted_answer_retains_the_full_parent_graph_without_displayed_citations()
    {
        var parent=Node();var child=Node();var source=child.Source with{Graph=new(1,[parent.Status.DocumentId],[parent]),CurrentPublication=child.Publication};
        var dependency=AiKnowledgeFaqGraphs.Dependency(child) with{Faq=source};var lineage=new AiKnowledgeAnswerLineage(6,Guid.CreateVersion7(),Guid.CreateVersion7(),Guid.CreateVersion7(),null,[dependency],[]);
        var json=AiKnowledgeAnswerLineageCodec.Encode(lineage);var restored=AiKnowledgeAnswerLineageCodec.Decode(json,lineage.MessageId,lineage.SessionId,lineage.ActorUserId,null);
        Assert.IsTrue(AiKnowledgeFaqPublishedSourceReader.Same(source,restored.Dependencies.Single().Faq));
        Assert.IsFalse(json.Contains("currentPublication",StringComparison.Ordinal));Assert.IsNull(restored.Dependencies.Single().Faq!.CurrentPublication);
    }
    [TestMethod] [DataRow("public","public")] [DataRow("internal","internal")] [DataRow("ancestor","restricted")]
    [DataRow("unrelated","public")] [DataRow("missing",null)] [DataRow("disabled",null)] [DataRow("unknown",null)]
    public void Parent_classification_uses_its_complete_current_libraries_and_rejects_incomplete_authority(string scenario,string? expected)
    {
        var knowledge=Guid.CreateVersion7();var leaf=Guid.CreateVersion7();var ancestor=Node();
        var dependency=new AiKnowledgeAnswerDependency(leaf,Guid.CreateVersion7(),Guid.CreateVersion7(),1,1,Guid.CreateVersion7(),Hash,Guid.CreateVersion7(),Hash,Hash,Hash,Guid.CreateVersion7());
        var origin=new AiKnowledgeFaqProvenance(1,"document_derived",null,[dependency]);
        var rows=new List<AiKnowledgeBaseRecord>{new(){Id=knowledge,Version=1,IsEnabled=true,DataClassification="public"},new(){Id=leaf,Version=2,IsEnabled=true,DataClassification="public"}};
        if(scenario=="internal")rows[1]=new(){Id=leaf,Version=2,IsEnabled=true,DataClassification="internal"};
        if(scenario=="missing")rows.RemoveAt(1);
        if(scenario=="disabled")rows[1]=new(){Id=leaf,Version=2,IsEnabled=false,DataClassification="public"};
        if(scenario=="unknown")rows[1]=new(){Id=leaf,Version=2,IsEnabled=true,DataClassification="Internal"};
        if(scenario is "ancestor" or "unrelated")rows.Add(new(){Id=ancestor.Status.KnowledgeBaseId,Version=3,IsEnabled=true,DataClassification="restricted"});
        if(scenario=="ancestor")origin=new(2,"faq_derived",null,[dependency]){Graph=new(1,[ancestor.Status.DocumentId],[ancestor])};
        var result=AiKnowledgeFaqClassification.ForParent(knowledge,origin,rows);Assert.AreEqual(expected,result?.Value);
        if(result is not null){Assert.AreEqual(knowledge,result.Root.Id);Assert.AreEqual(scenario=="ancestor"?3:2,result.Libraries.Count);}
    }
    [TestMethod] [DataRow(false,false)] [DataRow(false,true)] [DataRow(true,false)] [DataRow(true,true)]
    public void Final_parent_gate_jointly_binds_release_model_destination_and_every_library(bool mysql,bool tenant)
    {
        var provider=mysql?DatabaseProvider.MySql:DatabaseProvider.SqlServer;
        var sql=AiKnowledgeFaqDraftSql.CurrentFor(provider,tenant).Text;
        StringAssert.Contains(sql,"@FaqParentBindingsJson");StringAssert.Contains(sql,"managed.PublicationId=source.PublicationId");
        StringAssert.Contains(sql,"source.ActiveReceiptHash=");StringAssert.Contains(sql,"source.ActiveManifestHash=");
        StringAssert.Contains(sql,"input.ModelFingerprint=");StringAssert.Contains(sql,"approved.EndpointBaseUrl");StringAssert.Contains(sql,"approved.ApiKeyProtected");
        StringAssert.Contains(sql,"source.IsCurrent=1 AND source.StateKey='completed'");StringAssert.Contains(sql,"parentPublication.SnapshotHash=");
        Assert.AreEqual(tenant?SqlDataScope.TenantRequired:SqlDataScope.HostOnly,AiKnowledgeFaqParentSql.For(provider,tenant).Scope);
        StringAssert.Contains(sql,"proofLibrary.Version=parentLibrary.Version");StringAssert.Contains(sql,"proofLibrary.DataClassification=parentLibrary.DataClassification");
        StringAssert.Contains(sql,"SELECT "+AiKnowledgePublishedSourceSql.Columns+" FROM");
        // 检索及历史的末次查询也必须绑定祖先，不能只依赖前序单节点读取。
        StringAssert.Contains(AiKnowledgeRetrievalSql.CombinedHistoryFor(provider,tenant).Text,"@FaqParentBindingsJson");
        StringAssert.Contains(AiKnowledgeRetrievalSql.CombinedSourcesFor(provider,tenant,true).Text,"@FaqParentBindingsJson");
        Assert.AreEqual(tenant?SqlDataScope.TenantRequired:SqlDataScope.HostOnly,AiKnowledgeFaqDraftSql.CurrentFor(provider,tenant).Scope);
    }
}
