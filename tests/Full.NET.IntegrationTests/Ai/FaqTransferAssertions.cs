using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai.Contracts;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只验证传输新增边界；复用真实已发布来源，不重复运行旧草稿或发布断言。</summary>
internal static class FaqTransferAssertions
{
    internal static async Task<AiKnowledgeFaqTransferPackage> VerifyAsync(HttpClient client, DbConnection db, string connection, string owner, string reader,
        Guid knowledge, Guid document, Guid embedding, int embeddingVersion, Func<int> calls, Func<Task> replace,
        AiKnowledgeFaqTransferPackage? otherScope = null, Func<Guid,Task>? browser = null, Func<Task>? browserReplaced = null)
    {
        var root=$"/api/v1/ai/knowledge-bases/{knowledge}";var transfer=root+"/faq-transfer";var drafts=root+"/faq-drafts";
        var beforeCalls=-1;var budgets=-1;
        var content=new AiKnowledgeFaqContent("受控传输问题",["传输相似问题"],"保留全部原始来源的答案。","制度",null,null);
        var independent=await Send<AiKnowledgeFaqDraftMutation>(drafts+"/independent",new CreateAiKnowledgeFaqIndependentDraft(Guid.CreateVersion7(),content,"维护者独立依据"));
        var found=await Send<AiKnowledgeSearchResponse>("/api/v1/ai/knowledge-search",new SearchAiKnowledgeRequest([knowledge],[document],embedding,embeddingVersion,"明确 补写",2,false,null));
        var derived=await Send<AiKnowledgeFaqDraftMutation>(drafts+"/from-documents",new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(),content,new(embedding,embeddingVersion,found.Sources)));
        beforeCalls=calls();budgets=await Count("fn_ai_operation_budget");var files=await Count("fn_files_file");
        var package=await Send<AiKnowledgeFaqTransferPackage>(transfer+"/export",new ExportAiKnowledgeFaqs([independent.Workspace.Id,derived.Workspace.Id]));
        Assert.AreEqual("faq-transfer-v4",package.Format);Assert.AreEqual(2,package.Items.Count);Assert.AreEqual("document_derived",package.Items[1].Origin);
        Assert.AreEqual(derived.Workspace.SourceHash,package.Items[1].SourceHash);CollectionAssert.AreEqual(derived.Workspace.Sources.ToArray(),package.Items[1].Sources.ToArray());
        await Expect(transfer+"/export",new ExportAiKnowledgeFaqs([independent.Workspace.Id]),HttpStatusCode.Forbidden,reader);
        await Expect(transfer+"/preview",new PreviewAiKnowledgeFaqImport(package),HttpStatusCode.Forbidden,reader);
        await Expect(transfer+"/export",new ExportAiKnowledgeFaqs([independent.Workspace.Id,independent.Workspace.Id]),HttpStatusCode.BadRequest);
        var target=await Send<AiKnowledgeBaseResponse>("/api/v1/ai/knowledge-bases",new CreateAiKnowledgeBaseRequest("接收传输的知识库",null),HttpStatusCode.Created);
        using(var policy=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/ai/knowledge-bases/{target.Id}/policy"))
        {
            policy.Headers.Authorization=new("Bearer",owner);policy.Content=JsonContent.Create(new UpdateAiKnowledgePolicyRequest("restricted",null,null,null,null,target.Version));
            using var policyResponse=await client.SendAsync(policy);Assert.AreEqual(HttpStatusCode.OK,policyResponse.StatusCode,await policyResponse.Content.ReadAsStringAsync());
        }
        var targetTransfer=$"/api/v1/ai/knowledge-bases/{target.Id}/faq-transfer";
        var lower=await Send<AiKnowledgeBaseResponse>("/api/v1/ai/knowledge-bases",new CreateAiKnowledgeBaseRequest("拒绝分类降级的目标库",null),HttpStatusCode.Created);
        await Policy(lower,"public");
        var lowerTransfer=$"/api/v1/ai/knowledge-bases/{lower.Id}/faq-transfer";
        var beforeDenied=await Count("fn_ai_knowledge_faq");
        var rejected=await Send<AiKnowledgeFaqImportReport>(lowerTransfer+"/preview",new PreviewAiKnowledgeFaqImport(package));
        Assert.IsTrue(rejected.Items.All(item=>item.Status=="classification_conflict"));
        var rejectedCommit=await Send<AiKnowledgeFaqImportReport>(lowerTransfer+"/import",new ImportAiKnowledgeFaqs(package,rejected.Digest,[Guid.CreateVersion7(),Guid.CreateVersion7()]));
        Assert.IsTrue(rejectedCommit.Items.All(item=>item.Status=="classification_conflict"));
        Assert.AreEqual(beforeDenied,await Count("fn_ai_knowledge_faq"));
        await ClassificationRace();
        var countBefore=await Count("fn_ai_knowledge_faq");
        var preview=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/preview",new PreviewAiKnowledgeFaqImport(package));
        Assert.IsTrue(preview.Items.All(item=>item.Status=="ready"&&item.FaqId is null));Assert.AreEqual(countBefore,await Count("fn_ai_knowledge_faq"));
        var import=new ImportAiKnowledgeFaqs(package,preview.Digest,[Guid.CreateVersion7(),Guid.CreateVersion7()]);
        await Expect(targetTransfer+"/import",import with{ExpectedDigest=new('F',64)},HttpStatusCode.BadRequest);
        var imported=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/import",import);
        Assert.IsTrue(imported.Items.All(item=>item.Status=="created"&&item.FaqId.HasValue&&item.SavedRevisionId.HasValue));
        var replay=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/import",import);Assert.IsTrue(replay.Items.All(item=>item.Status=="replayed"));
        CollectionAssert.AreEqual(imported.Items.Select(item=>item.FaqId).ToArray(),replay.Items.Select(item=>item.FaqId).ToArray());Assert.AreEqual(countBefore+2,await Count("fn_ai_knowledge_faq"));
        var copied=await Get<AiKnowledgeFaqDraftWorkspace>($"/api/v1/ai/knowledge-bases/{target.Id}/faq-drafts/{imported.Items[1].FaqId}");
        // 接收库明确设为 restricted；当前宿主只批准 public/internal 导出，不能借同一来源绕过根分类。
        await Expect(targetTransfer+"/export",new ExportAiKnowledgeFaqs([imported.Items[0].FaqId!.Value]),HttpStatusCode.Conflict);
        Assert.AreEqual("document_derived",copied.Origin);Assert.AreEqual(derived.Workspace.SourceHash,copied.SourceHash);CollectionAssert.AreEqual(derived.Workspace.Sources.ToArray(),copied.Sources.ToArray());
        // 改成独立、删除叶、伪造正文或来源摘要，即使重新预览取得新摘要也不构成来源授权。
        foreach(var item in new[]{package.Items[1] with{Origin="independent",Basis="伪造独立依据",Sources=[]},package.Items[1] with{Content=content with{Answer="改写答案"}},package.Items[1] with{SourceHash=new('A',64)}})
        {
            var bad=package with{Items=[item]};var result=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/preview",new PreviewAiKnowledgeFaqImport(bad));
            Assert.AreEqual("source_unavailable",result.Items.Single().Status);
            var denied=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/import",new ImportAiKnowledgeFaqs(bad,result.Digest,[Guid.CreateVersion7()]));Assert.AreEqual("source_unavailable",denied.Items.Single().Status);
        }
        var mixed=package with{Items=[package.Items[0],package.Items[1] with{FaqId=Guid.CreateVersion7()}]};
        var mixedPreview=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/preview",new PreviewAiKnowledgeFaqImport(mixed));
        CollectionAssert.AreEqual(new[]{"ready","source_unavailable"},mixedPreview.Items.Select(item=>item.Status).ToArray());
        var partial=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/import",new ImportAiKnowledgeFaqs(mixed,mixedPreview.Digest,[Guid.CreateVersion7(),Guid.CreateVersion7()]));
        CollectionAssert.AreEqual(new[]{"created","source_unavailable"},partial.Items.Select(item=>item.Status).ToArray());
        if(otherScope is not null)
        {
            var foreign=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/preview",new PreviewAiKnowledgeFaqImport(otherScope));Assert.IsTrue(foreign.Items.All(item=>item.Status=="source_unavailable"));
        }
        if(browser is not null)await browser(knowledge);
        await replace();
        if(browserReplaced is not null)await browserReplaced();
        beforeCalls=calls();budgets=await Count("fn_ai_operation_budget");
        await Expect(transfer+"/export",new ExportAiKnowledgeFaqs([independent.Workspace.Id,derived.Workspace.Id]),HttpStatusCode.Conflict);
        var stale=await Send<AiKnowledgeFaqImportReport>(targetTransfer+"/preview",new PreviewAiKnowledgeFaqImport(package));CollectionAssert.AreEqual(new[]{"ready","source_unavailable"},stale.Items.Select(item=>item.Status).ToArray());
        await Get<AiKnowledgeFaqDraftWorkspace>($"/api/v1/ai/knowledge-bases/{target.Id}/faq-drafts/{imported.Items[0].FaqId}");
        await Expect($"/api/v1/ai/knowledge-bases/{target.Id}/faq-drafts/{imported.Items[1].FaqId}",null,HttpStatusCode.Conflict,get:true);
        // 来源替换本身收费，传输之前所有操作必须保持零新增模型调用及预算/Files 对象。
        Assert.AreEqual(files,await Count("fn_files_file"));return package;

        async Task<int> Count(string table)=>await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM "+table);
        async Task Policy(AiKnowledgeBaseResponse value,string classification)
        {
            using var request=new HttpRequestMessage(HttpMethod.Put,$"/api/v1/ai/knowledge-bases/{value.Id}/policy");
            request.Headers.Authorization=new("Bearer",owner);request.Content=JsonContent.Create(new UpdateAiKnowledgePolicyRequest(classification,null,null,null,null,value.Version));
            using var response=await client.SendAsync(request);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode,await response.Content.ReadAsStringAsync());
        }
        async Task ClassificationRace()
        {
            var destination=await Send<AiKnowledgeBaseResponse>("/api/v1/ai/knowledge-bases",new CreateAiKnowledgeBaseRequest("锁等待期间分类变更",null),HttpStatusCode.Created);
            await Policy(destination,"restricted");
            var path=$"/api/v1/ai/knowledge-bases/{destination.Id}/faq-transfer";
            var single=package with{Items=[package.Items[0]]};var ready=await Send<AiKnowledgeFaqImportReport>(path+"/preview",new PreviewAiKnowledgeFaqImport(single));
            Assert.AreEqual("ready",ready.Items.Single().Status);
            var mysql=db is MySqlConnection;
            await using var blocker=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql,connection);await blocker.OpenAsync();
            var observerConnection=mysql?new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()){Database=db.Database}.ConnectionString:connection;
            await using var observer=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql,observerConnection);await observer.OpenAsync();
            await using var held=await blocker.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            var released=false;Task<AiKnowledgeFaqImportReport>? pending=null;
            try
            {
                var lockSql=mysql?"SELECT Id FROM fn_ai_knowledge_base WHERE Id=@Id FOR UPDATE":"SELECT Id FROM fn_ai_knowledge_base WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id";
                Assert.AreEqual(destination.Id,await blocker.QuerySingleAsync<Guid>(lockSql,new{Id=destination.Id},held,commandTimeout:3));
                var session=await blocker.QuerySingleAsync<long>(mysql?"SELECT CONNECTION_ID()":"SELECT @@SPID",transaction:held);
                pending=Send<AiKnowledgeFaqImportReport>(path+"/import",new ImportAiKnowledgeFaqs(single,ready.Digest,[Guid.CreateVersion7()]));
                var waitSql=mysql?"SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waits INNER JOIN performance_schema.data_locks AS locks ON locks.ENGINE_LOCK_ID=waits.BLOCKING_ENGINE_LOCK_ID INNER JOIN performance_schema.threads AS holder ON holder.THREAD_ID=locks.THREAD_ID WHERE locks.OBJECT_SCHEMA=@Database AND locks.OBJECT_NAME='fn_ai_knowledge_base' AND holder.PROCESSLIST_ID=@Session":"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(@Database) AND blocking_session_id=@Session AND wait_type LIKE 'LCK%'";
                var until=DateTimeOffset.UtcNow.AddSeconds(10);var waiting=false;
                while(!pending.IsCompleted&&DateTimeOffset.UtcNow<until)
                {
                    waiting=await observer.QuerySingleAsync<int>(waitSql,new{Database=db.Database,Session=session},commandTimeout:2)>0;
                    if(waiting)break;await Task.Delay(25);
                }
                Assert.IsTrue(waiting,"必须观察真实目标库根锁等待，不能以延迟推测并发边界。");
                await blocker.ExecuteAsync("UPDATE fn_ai_knowledge_base SET DataClassification='public',Version=Version+1 WHERE Id=@Id",new{Id=destination.Id},held);
                await held.CommitAsync();released=true;
                Assert.AreEqual("conflict",(await pending.WaitAsync(TimeSpan.FromSeconds(30))).Items.Single().Status);
                Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq WHERE KnowledgeBaseId=@Id",new{Id=destination.Id}));
                Assert.AreEqual("classification_conflict",(await Send<AiKnowledgeFaqImportReport>(path+"/preview",new PreviewAiKnowledgeFaqImport(single))).Items.Single().Status);
                Console.WriteLine($"FAQ transfer classification root wait verified: {(mysql?"MySql":"SqlServer")}; stale approval rejected without writes.");
            }
            finally{if(!released)await held.RollbackAsync();if(pending is not null&&!pending.IsCompleted)try{await pending.WaitAsync(TimeSpan.FromSeconds(30));}catch(Exception){}}
        }
        async Task<T> Get<T>(string path){using var response=await Request(path,null,owner,true);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<T>())!;}
        async Task<T> Send<T>(string path,object body,HttpStatusCode status=HttpStatusCode.OK)
        {
            using var response=await Request(path,body,owner);Assert.AreEqual(status,response.StatusCode,await response.Content.ReadAsStringAsync());
            if(path.Contains("/faq-transfer",StringComparison.Ordinal))Assert.IsTrue(response.Headers.CacheControl?.NoStore==true);
            var result=(await response.Content.ReadFromJsonAsync<T>())!;
            if(beforeCalls>=0&&(path.EndsWith("/import",StringComparison.Ordinal)||path.EndsWith("/preview",StringComparison.Ordinal)||path.EndsWith("/export",StringComparison.Ordinal)))
            {Assert.AreEqual(beforeCalls,calls());Assert.AreEqual(budgets,await Count("fn_ai_operation_budget"));}
            return result;
        }
        async Task Expect(string path,object? body,HttpStatusCode status,string? actor=null,bool get=false){using var response=await Request(path,body,actor??owner,get);Assert.AreEqual(status,response.StatusCode,await response.Content.ReadAsStringAsync());}
        async Task<HttpResponseMessage> Request(string path,object? body,string actor,bool get=false){using var request=new HttpRequestMessage(get?HttpMethod.Get:HttpMethod.Post,path);request.Headers.Authorization=new("Bearer",actor);if(body is not null)request.Content=JsonContent.Create(body);return await client.SendAsync(request);}
    }
}
