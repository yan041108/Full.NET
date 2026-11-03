using System.Text.Json;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.Http;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Serialization;
using Microsoft.Extensions.Configuration;

namespace Full.NET.UnitTests.Ai;

/// <summary>传输包不授予来源权威；输入预算、精确快照和宿主导出政策均失败关闭。</summary>
[TestClass]
public sealed class AiKnowledgeFaqTransferTests
{
    [TestMethod]
    [DataRow("public","public",false,true)] [DataRow("public","internal",false,true)] [DataRow("public","restricted",false,true)]
    [DataRow("internal","public",false,false)] [DataRow("internal","internal",false,true)] [DataRow("internal","restricted",false,true)]
    [DataRow("restricted","public",false,false)] [DataRow("restricted","internal",false,false)] [DataRow("restricted","restricted",false,true)]
    [DataRow("restricted","internal",true,true)] [DataRow("unknown","public",false,false)] [DataRow("public","unknown",false,false)]
    [DataRow("unknown","unknown",true,false)] [DataRow("public","",false,false)]
    public void Faq_transfer_cannot_implicitly_lower_classification(string source,string target,bool same,bool expected)=>
        Assert.AreEqual(expected,AiKnowledgeFaqTransferPolicy.CanPlace(source,target,same));
    internal static AiKnowledgeFaqTransferPackage Package() => new(AiKnowledgeFaqTransferPolicy.Format,
        [new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "independent", "独立权威依据", new('A',64), new('B',64),
            new("原始问题", ["相似问题"], "原始答案", "制度", null, null), [])]);
    [TestMethod]
    public void Faq_transfer_roundtrips_exact_content_and_changes_digest_for_every_source_intent()
    {
        var package=Package();var json=JsonSerializer.Serialize(package,AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferPackage);
        var restored=JsonSerializer.Deserialize(json,AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferPackage)!;
        Assert.AreEqual(AiKnowledgeFaqTransferPolicy.Digest(package),AiKnowledgeFaqTransferPolicy.Digest(restored));
        Assert.IsTrue(AiKnowledgeFaqTransferPolicy.Same(package.Items[0],restored.Items[0]));
        foreach(var changed in new[]{package.Items[0] with {Origin="document_derived",Basis=null},package.Items[0] with{Content=package.Items[0].Content with{Answer="重写答案"}},package.Items[0] with{SourceHash=new('C',64)},package.Items[0] with{RevisionId=Guid.CreateVersion7()}})
            Assert.IsFalse(AiKnowledgeFaqTransferPolicy.Same(package.Items[0],changed));
    }
    [TestMethod] [DataRow("version")] [DataRow("empty")] [DataRow("many")] [DataRow("duplicate")] [DataRow("basis")] [DataRow("derived")] [DataRow("unknown")] [DataRow("hash")] [DataRow("scope")]
    public void Faq_transfer_rejects_unknown_incomplete_duplicate_or_unbounded_packages(string fault)
    {
        var package=Package();var item=package.Items[0];
        package=fault switch{
            "version"=>package with{Format="future"},"empty"=>package with{Items=[]},"many"=>package with{Items=Enumerable.Range(0,9).Select(_=>item with{FaqId=Guid.CreateVersion7()}).ToArray()},
            "duplicate"=>package with{Items=[item,item]},"scope"=>package with{Items=[item,item with{FaqId=Guid.CreateVersion7(),KnowledgeBaseId=Guid.CreateVersion7()}]},
            "basis"=>package with{Items=[item with{Basis=null}]},"derived"=>package with{Items=[item with{Origin="document_derived",Basis=null}]},"unknown"=>package with{Items=[item with{Origin="faq_derived"}]},_=>package with{Items=[item with{SourceHash="short"}]}};
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFaqTransferPolicy.Digest(package));
    }
    [TestMethod]
    public void Faq_transfer_enforces_total_unicode_budget_without_truncating_items()
    {
        var package=Package();var max=new AiKnowledgeFaqContent(new('问',512),Enumerable.Range(0,16).Select(i=>new string('别',511)+(char)('A'+i)).ToArray(),new('答',8000),new('类',64),null,null);
        var items=Enumerable.Range(0,8).Select(_=>package.Items[0] with{FaqId=Guid.CreateVersion7(),Content=max}).ToArray();
        Assert.ThrowsExactly<InvalidDataException>(()=>AiKnowledgeFaqTransferPolicy.Digest(package with{Items=items}));
        Assert.AreEqual(64,AiKnowledgeFaqTransferPolicy.Digest(package with{Items=[items[0]]}).Length);
    }
    [TestMethod]
    public void Faq_transfer_strict_input_rejects_hidden_source_replacement_fields()
    {
        var json=JsonSerializer.Serialize(Package(),AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferPackage);
        Assert.ThrowsExactly<JsonException>(()=>JsonSerializer.Deserialize(json[..^1]+",\"tenantId\":null}",AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferPackage));
        Assert.ThrowsExactly<JsonException>(()=>JsonSerializer.Deserialize(json.Replace("\"faqId\":","\"authorityOverride\":true,\"faqId\":"),AiKnowledgeFaqJsonContext.Default.AiKnowledgeFaqTransferPackage));
    }
    [TestMethod]
    public void Faq_export_policy_has_independent_host_classification_boundary()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["FullNet:Ai:DataPolicy:export:0"]="public"}).Build();
        var policy=AiDataPolicyConfiguration.Read(configuration);
        Assert.IsTrue(policy.Allows("export","public"));Assert.IsFalse(policy.Allows("export","restricted"));Assert.IsTrue(policy.Allows("context","restricted"));
    }
}
