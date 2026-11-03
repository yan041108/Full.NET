using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实多级派生、修订/复制/传输及父发布撤销；原文叶继续有效，因而不能靠平面叶过滤通过。</summary>
internal static class FaqParentAssertions
{
    internal static async Task VerifyAsync(HttpClient client, DbConnection db, Guid knowledge, Guid leaf,
        AiKnowledgeFaqDraftWorkspace first, Guid firstPublication, Guid embedding, int embeddingVersion, Guid generation, int generationVersion,
        string owner, string reader, Guid[] members, Func<AiKnowledgeFaqDraftWorkspace,Task<AiKnowledgeFaqPublication>> publish,
        Func<AiKnowledgeSearchSource,Task>? browserReady=null, Func<Task>? browserRevoked=null,
        Func<AiKnowledgeSearchSource,Task<(Guid SessionId,Guid MessageId)>>? seedHistory=null)
    {
        var root=$"/api/v1/ai/knowledge-bases/{knowledge}";var drafts=root+"/faq-drafts";
        var child=await Derive(first.ReservedDocumentId,"二级派生标准 FAQ");
        Assert.AreEqual("faq_derived",child.Origin);Assert.AreEqual(first.ReservedDocumentId,child.Graph!.Roots.Single());Assert.AreEqual(1,child.Graph.Nodes.Count);
        Assert.IsTrue(child.Sources.Any(s=>s.DocumentId==leaf&&s.SourceKind=="document-v1"));
        // 草稿读取没有自己的索引模型准入；同版本父模型漂移也必须由父资格门禁独立拒绝。
        var originalOrganization=await db.QuerySingleAsync<string?>("SELECT OrganizationId FROM fn_ai_model_config WHERE Id=@Id",new{Id=embedding});
        var beforeModelDrift=await Count();
        await db.ExecuteAsync("UPDATE fn_ai_model_config SET OrganizationId='unapproved-parent-org' WHERE Id=@Id",new{Id=embedding});
        try{await Denied(HttpMethod.Get,drafts+"/"+child.Id,null);Assert.AreEqual(beforeModelDrift,await Count());}
        finally{await db.ExecuteAsync("UPDATE fn_ai_model_config SET OrganizationId=@Organization WHERE Id=@Id",new{Organization=originalOrganization,Id=embedding});}
        // 即使所有叶仍有效，父发布的损坏快照也不是服务资格，且不能变成未处理的 JSON 500。
        var originalSnapshot=await db.QuerySingleAsync<string>("SELECT SnapshotJson FROM fn_ai_knowledge_publication WHERE Id=@Id",new{Id=firstPublication});
        var originalSnapshotHash=await db.QuerySingleAsync<string>("SELECT SnapshotHash FROM fn_ai_knowledge_publication WHERE Id=@Id",new{Id=firstPublication});
        var malformedHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("{")));
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET SnapshotJson='{',SnapshotHash=@Hash WHERE Id=@Id",new{Hash=malformedHash,Id=firstPublication});
        try{await Denied(HttpMethod.Get,drafts+"/"+child.Id,null);Assert.AreEqual(beforeModelDrift,await Count());}
        finally{await db.ExecuteAsync("UPDATE fn_ai_knowledge_publication SET SnapshotJson=@Snapshot,SnapshotHash=@Hash WHERE Id=@Id",new{Snapshot=originalSnapshot,Hash=originalSnapshotHash,Id=firstPublication});}
        var original=child;
        child=(await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Put,drafts+"/"+child.Id,owner,
            new SaveAiKnowledgeFaqDraft(Guid.CreateVersion7(),child.Revision.Id,child.HeadVersion,child.Revision.Content with{Answer="编辑后仍保留父资格"}))).Workspace;
        child=(await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,drafts+$"/{child.Id}/restore",owner,
            new RestoreAiKnowledgeFaqDraft(Guid.CreateVersion7(),child.Revision.Id,child.HeadVersion,original.Revision.Id))).Workspace;
        Assert.AreEqual(original.SourceHash,child.SourceHash);Assert.AreEqual(3,child.HeadVersion);
        var copied=await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,drafts+$"/{child.Id}/copy",owner,new CopyAiKnowledgeFaqDraft(Guid.CreateVersion7(),child.Revision.Id,child.HeadVersion));
        Assert.AreEqual(child.SourceHash,copied.Workspace.SourceHash);Assert.AreEqual(first.ReservedDocumentId,copied.Workspace.Graph!.Roots.Single());
        _=await publish(child);
        var grandchild=await Derive(child.ReservedDocumentId,"三级派生标准 FAQ");var published=await publish(grandchild);
        Assert.AreEqual(2,grandchild.Graph!.Nodes.Count);Assert.AreEqual(child.ReservedDocumentId,grandchild.Graph.Roots.Single());
        Assert.AreEqual(first.ReservedDocumentId,grandchild.Graph.Nodes.Single(n=>n.Source.DocumentId==child.ReservedDocumentId).ParentDocumentIds.Single());
        var delivered=await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get,root+"/faqs/"+grandchild.Id,reader);
        Assert.AreEqual(2,delivered.Graph!.Nodes.Count);Assert.AreEqual(3,delivered.Sources.Count);
        var before=await Count();var files=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file");
        var package=await Send<AiKnowledgeFaqTransferPackage>(HttpMethod.Post,root+"/faq-transfer/export",owner,new ExportAiKnowledgeFaqs([grandchild.Id]));
        Assert.AreEqual("faq-transfer-v4",package.Format);Assert.AreEqual(2,package.Items.Single().Graph!.Nodes.Count);
        var preview=await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post,root+"/faq-transfer/preview",owner,new PreviewAiKnowledgeFaqImport(package));
        var imported=await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post,root+"/faq-transfer/import",owner,new ImportAiKnowledgeFaqs(package,preview.Digest,[Guid.CreateVersion7()]));
        Assert.AreEqual("created",imported.Items.Single().Status);
        var newDraft=await Send<AiKnowledgeFaqDraftWorkspace>(HttpMethod.Get,drafts+"/"+imported.Items.Single().FaqId,owner);
        Assert.AreEqual(grandchild.SourceHash,newDraft.SourceHash);Assert.AreEqual(2,newDraft.Graph!.Nodes.Count);
        // 改成普通派生并删除图不能取得导入权威；服务器仍比较准确来源摘要及整张图。
        var forged=package with{Items=[package.Items[0] with{Origin="document_derived",Graph=null,Sources=package.Items[0].Sources.Where(s=>s.SourceKind=="document-v1").ToArray()}]};
        var rejected=await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post,root+"/faq-transfer/preview",owner,new PreviewAiKnowledgeFaqImport(forged));
        Assert.AreEqual("source_unavailable",rejected.Items.Single().Status);Assert.AreEqual(before,await Count());Assert.AreEqual(files,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        await FaqSearchAssertions.VerifyAsync(client,db,knowledge,leaf,grandchild.Id,grandchild.ReservedDocumentId,grandchild.Revision.Id,published.Id,
            embedding,embeddingVersion,generation,generationVersion,owner,reader,members,async()=>
            {
                await Send<AiKnowledgePublicationHeadResponse>(HttpMethod.Post,drafts+$"/{first.Id}/publications/withdraw",owner,new WithdrawAiKnowledgePublication(1,firstPublication));
                var ordinary=await Search(leaf);Assert.AreEqual("document-v1",ordinary.Sources.Single().SourceKind);
                foreach(var draft in new[]{child.Id,grandchild.Id,copied.Workspace.Id,newDraft.Id})
                    await Denied(HttpMethod.Get,drafts+"/"+draft,null);
                await Denied(HttpMethod.Post,root+"/faq-transfer/export",new ExportAiKnowledgeFaqs([grandchild.Id]));
                var unavailable=await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post,root+"/faq-transfer/preview",owner,new PreviewAiKnowledgeFaqImport(package));
                Assert.AreEqual("source_unavailable",unavailable.Items.Single().Status);
                var staleSource=grandchild.Graph.Nodes.Single(n=>n.Source.DocumentId==grandchild.Graph.Roots.Single()).Source;
                await Denied(HttpMethod.Post,drafts+"/from-sources",new CreateAiKnowledgeFaqDerivedDraft(Guid.CreateVersion7(),grandchild.Revision.Content,new(embedding,embeddingVersion,[staleSource])));
            },browserReady,browserRevoked,seedHistory,async(session,message)=>
            {
                var originalLineage=await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id",new{Id=message});
                var flattened=System.Text.Json.Nodes.JsonNode.Parse(originalLineage)!;
                var metadata=flattened["dependencies"]![0]!["faq"]!.AsObject();Assert.AreEqual(2,metadata["graph"]!["nodes"]!.AsArray().Count);metadata.Remove("graph");flattened["version"]=5;
                await db.ExecuteAsync("UPDATE fn_ai_chat_message SET KnowledgeLineage=@Lineage WHERE Id=@Id",new{Lineage=flattened.ToJsonString(),Id=message});
                try
                {
                    var denied=await Send<AiChatSessionResponse>(HttpMethod.Get,$"/api/v1/ai/chat/sessions/{session}",reader);
                    var hidden=denied.Messages.Single(m=>m.Id==message);Assert.IsNotNull(hidden.KnowledgeUnavailableCode);Assert.AreEqual("",hidden.Content);Assert.AreEqual(0,hidden.Citations.Count);
                }
                finally{await db.ExecuteAsync("UPDATE fn_ai_chat_message SET KnowledgeLineage=@Lineage WHERE Id=@Id",new{Lineage=originalLineage,Id=message});}
            },expectedLineageVersion:6);
        var lineages=await db.QueryAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE KnowledgeLineage IS NOT NULL");
        Assert.IsTrue(lineages.Any(json=>{using var parsed=JsonDocument.Parse(json);return parsed.RootElement.GetProperty("dependencies").EnumerateArray().Any(d=>d.TryGetProperty("faq",out var faq)&&faq.TryGetProperty("graph",out var graph)&&graph.GetProperty("nodes").GetArrayLength()==2);}));

        async Task<AiKnowledgeFaqDraftWorkspace> Derive(Guid parent,string question)
        {
            var selection=await Search(parent);Assert.AreEqual("faq-revision-v1",selection.Sources.Single().SourceKind);
            var content=first.Revision.Content with{Question=question,Aliases=[],Answer="保留真实多级父 FAQ 和全部文档叶的标准答案。"};
            if(parent==first.ReservedDocumentId)
            {
                var noCost=await Count();using var denied=new HttpRequestMessage(HttpMethod.Post,drafts+"/from-sources");denied.Headers.Authorization=new("Bearer",reader);
                denied.Content=JsonContent.Create(new CreateAiKnowledgeFaqDerivedDraft(Guid.CreateVersion7(),content,new(embedding,embeddingVersion,selection.Sources)));
                using var forbidden=await client.SendAsync(denied);Assert.AreEqual(HttpStatusCode.Forbidden,forbidden.StatusCode,await forbidden.Content.ReadAsStringAsync());Assert.AreEqual(noCost,await Count());
            }
            return (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,drafts+"/from-sources",owner,
                new CreateAiKnowledgeFaqDerivedDraft(Guid.CreateVersion7(),content,new(embedding,embeddingVersion,selection.Sources)))).Workspace;
        }
        Task<AiKnowledgeSearchResponse> Search(Guid document)=>Send<AiKnowledgeSearchResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-search",reader,
            new SearchAiKnowledgeRequest([knowledge],[document],embedding,embeddingVersion,"标准",20,false,null));
        Task<int> Count()=>db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
        async Task<T> Send<T>(HttpMethod method,string path,string actor,object? body=null)
        {
            using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new("Bearer",actor);if(body is not null)request.Content=JsonContent.Create(body);
            using var response=await client.SendAsync(request);Assert.IsTrue(response.IsSuccessStatusCode,$"{method} {path}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        async Task Denied(HttpMethod method,string path,object? body)
        {
            using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new("Bearer",owner);if(body is not null)request.Content=JsonContent.Create(body);
            using var response=await client.SendAsync(request);Assert.AreEqual(HttpStatusCode.Conflict,response.StatusCode,await response.Content.ReadAsStringAsync());
        }
    }
}
