using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>手工来源草稿绑定实际本人对象；完整传输和后台索引不能授予独立审核者他人对象权限。</summary>
internal static class PersonalScheduleFaqAssertions
{
    internal static async Task VerifyAsync(HttpClient client, DbConnection db, string owner, string reviewer, Guid sourceKnowledge, Guid document,
        Guid embedding, int embeddingVersion, Guid generation, int generationVersion, Func<AiKnowledgeFaqIndexAdmission,Task> ready)
    {
        const string calendar = "/api/v1/calendar/my-personal-schedules";
        var now = DateTimeOffset.UtcNow;
        var personal = await Send<PersonalScheduleResponse>(HttpMethod.Post, calendar, owner, new CreatePersonalScheduleRequest("私人FAQ来源，不能通过复制清空。", now, now), HttpStatusCode.Created);
        var preview = await Send<AiPersonalScheduleSourcePreview>(HttpMethod.Post, "/api/v1/ai/personal-schedule-assistant/sources/preview", owner, new PreviewAiPersonalScheduleSources([personal.Id]));
        var proof = preview.Sources.Single(); var expected = new AiPersonalScheduleSourceSelection(proof.Id,proof.Version,proof.ProjectionHash);
        var sources = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-search", owner,
            new SearchAiKnowledgeRequest([sourceKnowledge],[document],embedding,embeddingVersion,"识别",2,false,null));
        var source = sources.Sources.Single(value => value.DocumentId == document);
        var target = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-bases",owner,new CreateAiKnowledgeBaseRequest("本人日程FAQ目标",null),HttpStatusCode.Created);
        var root=$"/api/v1/ai/knowledge-bases/{target.Id}"; var drafts=root+"/faq-drafts";
        target=await Send<AiKnowledgeBaseResponse>(HttpMethod.Put,root+"/policy",owner,new UpdateAiKnowledgePolicyRequest("restricted",embedding,embeddingVersion,generation,generationVersion,target.Version));
        using var claims=Payload(reviewer); var reader=Guid.Parse(claims.RootElement.GetProperty("sub").GetString()!);
        await Send<AiKnowledgeMembersResponse>(HttpMethod.Put,root+"/members",owner,new SetAiKnowledgeMembersRequest([reader],target.Version));
        var beforeBudget=await Count("fn_ai_operation_budget");var files=await Count("fn_files_file");
        var content=new AiKnowledgeFaqContent("本人日程手工标准问题",[],"人工核对的标准答案。","私人制度",null,null);
        var selection=new AiKnowledgeFaqDocumentSelection(embedding,embeddingVersion,[source]) {PersonalSchedules=[expected]};
        var request=new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(),content,selection);
        var created=await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,drafts+"/from-documents",owner,request);
        var draft=created.Workspace; AssertProof(draft);Assert.AreEqual("document_derived",draft.Origin);
        var original=draft;var path=drafts+"/"+draft.Id;
        var replay=await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,drafts+"/from-documents",owner,request);Assert.IsTrue(replay.Replayed);Assert.AreEqual(draft.Id,replay.Workspace.Id);
        draft=(await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Put,path,owner,new SaveAiKnowledgeFaqDraft(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion,content with {Answer="已修改，来源仍不可变"}))).Workspace;AssertProof(draft);
        draft=(await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,path+"/restore",owner,new RestoreAiKnowledgeFaqDraft(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion,original.Revision.Id))).Workspace;AssertProof(draft);
        var copy=await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,path+"/copy",owner,new CopyAiKnowledgeFaqDraft(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion));AssertProof(copy.Workspace);
        Assert.AreEqual(original.SourceHash,draft.SourceHash);Assert.AreEqual(draft.SourceHash,copy.Workspace.SourceHash);
        var raw=await db.QuerySingleAsync<string>("SELECT SourceJson FROM fn_ai_knowledge_faq WHERE Id=@Id",new{draft.Id});
        using(var encoded=JsonDocument.Parse(raw))
        {
            Assert.AreEqual(4,encoded.RootElement.GetProperty("version").GetInt32());
            Assert.AreEqual(1,encoded.RootElement.GetProperty("personalSchedules").GetArrayLength());
            Assert.AreEqual(proof.Id,encoded.RootElement.GetProperty("personalSchedules")[0].GetProperty("id").GetGuid());
            Assert.IsFalse(raw.Contains(personal.Content,StringComparison.Ordinal));
        }
        var package=await Send<AiKnowledgeFaqTransferPackage>(HttpMethod.Post,root+"/faq-transfer/export",owner,new ExportAiKnowledgeFaqs([draft.Id]));
        Assert.AreEqual("faq-transfer-v4",package.Format);Assert.AreEqual(expected,package.Items.Single().PersonalSchedules!.Single());
        var previewImport=await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post,root+"/faq-transfer/preview",owner,new PreviewAiKnowledgeFaqImport(package));
        Assert.AreEqual("ready",previewImport.Items.Single().Status);
        var imported=await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post,root+"/faq-transfer/import",owner,new ImportAiKnowledgeFaqs(package,previewImport.Digest,[Guid.CreateVersion7()]));
        Assert.AreEqual("created",imported.Items.Single().Status);
        var importedDraft=await Send<AiKnowledgeFaqDraftWorkspace>(HttpMethod.Get,drafts+"/"+imported.Items[0].FaqId,owner);AssertProof(importedDraft);
        var revisions=await Count("fn_ai_knowledge_faq_revision");
        await Denied(HttpMethod.Post,drafts+"/from-documents",owner,request with {RequestKey=Guid.CreateVersion7(),Selection=selection with {PersonalSchedules=[expected with {Version=2}]}});
        await Denied(HttpMethod.Post,"/api/v1/ai/personal-schedule-assistant/sources/preview",reviewer,new PreviewAiPersonalScheduleSources([personal.Id]));
        Assert.AreEqual(revisions,await Count("fn_ai_knowledge_faq_revision"));Assert.AreEqual(beforeBudget,await Count("fn_ai_operation_budget"));Assert.AreEqual(files,await Count("fn_files_file"));
        // 只有显式确认才索引；实际Worker必须按原持久会话调用Calendar后台核证，而不能借交互HttpContext。
        var admission=await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post,path+"/index",owner,new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion,true),HttpStatusCode.Accepted);
        await ready(admission);
        var lifecycle=await Send<AiKnowledgeFaqLifecycle>(HttpMethod.Get,path+"/lifecycle",owner);Assert.AreEqual("ready",lifecycle.IndexState);
        var proposal=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,path+"/publication-requests",owner,
            new RequestAiKnowledgeFaqPublication(Guid.CreateVersion7(),draft.Revision.Id,admission.VersionId,draft.HeadVersion,0,now.AddMinutes(-1),now.AddDays(1)),HttpStatusCode.Created);
        Assert.AreEqual(expected,proposal.PersonalSchedules!.Single());
        await Denied(HttpMethod.Get,path+"/publications/"+proposal.Id,reviewer,null);
        await Denied(HttpMethod.Post,path+$"/publications/{proposal.Id}/review",reviewer,new ReviewAiKnowledgePublication(true,proposal.Version,proposal.SnapshotHash));
        Assert.AreEqual("pending",await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_publication WHERE Id=@Id",new{proposal.Id}));
        Assert.AreEqual(files,await Count("fn_files_file"));
        // 正式来源变化后，读取/修订/复制/恢复/索引/导出与原包重新导入均当前复核。
        await Send<PersonalScheduleResponse>(HttpMethod.Put,calendar+"/"+personal.Id,owner,new UpdatePersonalScheduleRequest("已改变的本人来源",now,now,personal.Version));
        var stoppedBudget=await Count("fn_ai_operation_budget");var stoppedRevisions=await Count("fn_ai_knowledge_faq_revision");
        await Denied(HttpMethod.Get,path,owner,null);
        await Denied(HttpMethod.Put,path,owner,new SaveAiKnowledgeFaqDraft(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion,content));
        await Denied(HttpMethod.Post,path+"/copy",owner,new CopyAiKnowledgeFaqDraft(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion));
        await Denied(HttpMethod.Post,path+"/restore",owner,new RestoreAiKnowledgeFaqDraft(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion,original.Revision.Id));
        await Denied(HttpMethod.Post,path+"/index",owner,new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(),draft.Revision.Id,draft.HeadVersion,true));
        await Denied(HttpMethod.Post,root+"/faq-transfer/export",owner,new ExportAiKnowledgeFaqs([draft.Id]));
        var stale=await Send<AiKnowledgeFaqImportReport>(HttpMethod.Post,root+"/faq-transfer/preview",owner,new PreviewAiKnowledgeFaqImport(package));Assert.AreEqual("source_unavailable",stale.Items.Single().Status);
        Assert.AreEqual(stoppedRevisions,await Count("fn_ai_knowledge_faq_revision"));Assert.AreEqual(stoppedBudget,await Count("fn_ai_operation_budget"));

        void AssertProof(AiKnowledgeFaqDraftWorkspace value) {Assert.AreEqual(expected,value.PersonalSchedules!.Single());Assert.AreEqual(source.DocumentId,value.Sources.Single().DocumentId);}
        Task<int> Count(string table) => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM "+table);
        async Task<T> Send<T>(HttpMethod method,string url,string token,object? body=null,HttpStatusCode status=HttpStatusCode.OK)
        {using var message=new HttpRequestMessage(method,url);message.Headers.Authorization=new("Bearer",token);if(body is not null)message.Content=JsonContent.Create(body);using var response=await client.SendAsync(message);var text=await response.Content.ReadAsStringAsync();Assert.AreEqual(status,response.StatusCode,method+" "+url+" "+text);return JsonSerializer.Deserialize<T>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;}
        async Task Denied(HttpMethod method,string url,string token,object? body)
        {using var message=new HttpRequestMessage(method,url);message.Headers.Authorization=new("Bearer",token);if(body is not null)message.Content=JsonContent.Create(body);using var response=await client.SendAsync(message);Assert.IsTrue(response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden or HttpStatusCode.NotFound,method+" "+url+" "+await response.Content.ReadAsStringAsync());}
    }
    private static JsonDocument Payload(string token){var text=token.Split('.')[1].Replace('-','+').Replace('_','/');return JsonDocument.Parse(Convert.FromBase64String(text+new string('=',(4-text.Length%4)%4)));}
}
