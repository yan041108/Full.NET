using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>持久化状态夹具只证明运营元数据，不把构造的索引/发布记录当作真实处理或内容服务资格。</summary>
internal static class KnowledgeOperationsScenario
{
    internal static async Task VerifyAsync(DatabaseProvider provider,bool native)
    {
        var connection=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings=new Dictionary<string,string?> { ["DataProtection:KeyRingPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k05-keys",Guid.CreateVersion7().ToString("N")),
            ["Files:Local:RootPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k05-files",Guid.CreateVersion7().ToString("N")) };
        var bridge=native&&provider==DatabaseProvider.MySql?Environment.GetEnvironmentVariable("FULLNET_K05_BROWSER_BRIDGE"):null;
        if(bridge is not null){Directory.CreateDirectory(bridge);settings["Identity:AllowedOrigins:1"]="http://localhost:34492";settings["Tenancy:HostDomains:1"]="127.0.0.1";}
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,connection);
        if(Environment.GetEnvironmentVariable("FULLNET_K05_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=provider.ToString(),database=db.Database,native,filesRoot=settings["Files:Local:RootPath"],keysRoot=settings["DataProtection:KeyRingPath"]})+"\n");
        using var factory=new FullNetApiFactory(provider,connection,settingsOverrides:settings);await factory.InitializeAsync();
        var permissions=new[]{"identity.navigation.read","platform.dashboard.read",AiKnowledgeOperationsPermissions.Read,AiKnowledgePermissions.Create,AiKnowledgePermissions.Read,AiKnowledgePermissions.MembersUpdate,
            AiKnowledgeDocumentPermissions.Read,AiKnowledgeDocumentPermissions.SourceRead,AiKnowledgeDocumentPermissions.IndexRead,AiKnowledgePublicationPermissions.Read,AiKnowledgeFaqPermissions.Read,AiKnowledgeFaqPermissions.Write};
        var name="operations-owner-"+Guid.CreateVersion7().ToString("N");var curator=await factory.CreateHostIdentityAsync(name,permissions,password:FullNetApiFactory.TestPassword);
        var otherName="operations-other-"+Guid.CreateVersion7().ToString("N");var other=await factory.CreateHostIdentityAsync(otherName,permissions,password:FullNetApiFactory.TestPassword);
        var readerName="operations-reader-"+Guid.CreateVersion7().ToString("N");await factory.CreateHostIdentityAsync(readerName,permissions.Where(p=>p!=AiKnowledgeOperationsPermissions.Read).ToArray(),password:FullNetApiFactory.TestPassword);
        var artifact=native?NativeApiArtifactLocator.RequireArtifact():null;
        await using var api=artifact is null?null:await NativeApiProcessHost.StartAsync(artifact,provider,connection,settings,NativeAotTestTimeouts.ProcessStartup,
            listenAddress:bridge is null?null:new Uri("http://0.0.0.0:64215/"));
        using var client=api?.CreateClient()??factory.CreateClientForHost("localhost");using var curatorClient=api?.CreateClient()??factory.CreateClientForHost("localhost");
        using var otherClient=api?.CreateClient()??factory.CreateClientForHost("localhost");using var readerClient=api?.CreateClient()??factory.CreateClientForHost("localhost");
        var admin=await NativeApiE2EAssertions.LoginAsync(client,api?.LogFilePath??"K05 JIT");
        var actor=await IntegrationTestAuthHelper.LoginAsHostUserAsync(curatorClient,name);var otherToken=await IntegrationTestAuthHelper.LoginAsHostUserAsync(otherClient,otherName);
        var reader=await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient,readerName);await db.OpenAsync();
        if(!native&&Environment.GetEnvironmentVariable("FULLNET_K05_OPENAPI_EXPORT_DIR") is {Length:>0} export)
        {Directory.CreateDirectory(export);using var response=await client.GetAsync("/openapi/v1.json");response.EnsureSuccessStatusCode();await File.WriteAllTextAsync(Path.Combine(export,"k05-operations-"+provider.ToString().ToLowerInvariant()+".json"),await response.Content.ReadAsStringAsync());}
        await Expect("/api/v1/ai/knowledge-operations",reader,HttpStatusCode.Forbidden);
        foreach(var tenant in new[]{false,true})
        {
            var tenantId=tenant?(await Send<TenantContextSummary[]>(HttpMethod.Get,"/api/v1/tenancy/available",admin)).Single(t=>t.Identifier=="acme").Id:(Guid?)null;
            if(tenant){admin=(await Send<TenantContextTokenResponse>(HttpMethod.Put,"/api/v1/tenancy/context",admin,new ChangeTenantContextRequest(tenantId))).AccessToken;actor=admin;}
            var root=await Send<AiKnowledgeBaseResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-bases",actor,new CreateAiKnowledgeBaseRequest("运营元数据夹具",null),HttpStatusCode.Created);
            var now=DateTimeOffset.UtcNow.AddSeconds(-2);var ids=new List<Guid>();var docs=new List<Guid>();
            for(var i=0;i<28;i++)
            {
                var doc=Guid.CreateVersion7();var version=Guid.CreateVersion7();var task=Guid.CreateVersion7();docs.Add(doc);ids.Add(version);
                await SeedVersion(doc,version,root.Id,i==0?"completed":i==1?"failed":i==2?"running":"queued",now);
                if(i==0)await SeedPublication(doc,version,task,now,now.AddDays(-1),now.AddHours(-1));
            }
            var faq=(await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,$"/api/v1/ai/knowledge-bases/{root.Id}/faq-drafts/independent",actor,
                new CreateAiKnowledgeFaqIndependentDraft(Guid.CreateVersion7(),new("PRIVATE_FAQ_QUESTION",[],"PRIVATE_FAQ_ANSWER","制度",null,null),"人工独立测试依据"))).Workspace;
            var faqVersion=Guid.CreateVersion7();await SeedVersion(faq.ReservedDocumentId,faqVersion,root.Id,"queued",now,faq.Revision.Id);
            var downgraded=Guid.CreateVersion7();await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc,ParsingPolicyKey) VALUES (@Id,@Doc,@File,2,'PRIVATE_BAD_BINDING','text/plain',@Hash,1,@Actor,@Now,'default')",
                new{Id=downgraded,Doc=faq.ReservedDocumentId,File=Guid.CreateVersion7(),Hash=new string('c',64),Actor=curator.UserId,Now=now});
            // 管理页面不能交付这些标题/文件名，也不把虚构文件标识变成可下载的 Files 证明。
            var path="/api/v1/ai/knowledge-operations?knowledgeBaseId="+root.Id;
            var first=await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path,actor);Assert.AreEqual(25,first.Items.Count);Assert.IsNotNull(first.NextAfterId);
            var second=await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path+"&afterId="+first.NextAfterId+"&fromUtc="+Uri.EscapeDataString(first.FromUtc.ToString("o"))+"&toUtc="+Uri.EscapeDataString(first.ToUtc.ToString("o")),actor);
            Assert.AreEqual(4,second.Items.Count);Assert.IsNull(second.NextAfterId);Assert.AreEqual(29,first.Items.Concat(second.Items).Select(x=>x.VersionId).Distinct().Count());
            var combined=first.Items.Concat(second.Items).ToArray();Assert.IsTrue(combined.All(x=>x.KnowledgeBaseId==root.Id));Assert.IsFalse(combined.Any(x=>x.VersionId==downgraded));
            var faqRow=combined.Single(x=>x.SourceKind=="faq-revision-v1");Assert.AreEqual(faq.Id,faqRow.FaqId);Assert.AreEqual(faqVersion,faqRow.VersionId);Assert.AreEqual("static_revision",faqRow.PreparationState);
            var failed=await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path+"&view=failed",actor);Assert.AreEqual(2,failed.Items.Count);
            Assert.IsTrue(failed.Items.Any(x=>x.TaskState=="failed"&&x.FailureCode=="ai.knowledge.index_unavailable"));Assert.IsTrue(failed.Items.Any(x=>x.TaskState=="lease_expired"));
            var expired=await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path+"&view=expired",actor);Assert.AreEqual(1,expired.Items.Count);Assert.AreEqual("expired_record",expired.Items[0].PublicationState);Assert.AreEqual("completed",expired.Items[0].TaskState);
            await Expect(path+"&tenantId="+Guid.CreateVersion7(),actor,HttpStatusCode.BadRequest);await Expect(path+"&view=all&view=failed",actor,HttpStatusCode.BadRequest);
            await Expect(path+"&view=bogus",actor,HttpStatusCode.BadRequest);await Expect(path+"&fromUtc=2026-01-01T00:00:00Z&toUtc=2026-03-01T00:00:00Z",actor,HttpStatusCode.BadRequest);
            if(!tenant)
            {
                Assert.AreEqual(0,(await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path,otherToken)).Items.Count);
                // 成为普通库成员仍不是运营维护者；超级管理权也不绕过当前库所有者。
                await Send<AiKnowledgeMembersResponse>(HttpMethod.Put,$"/api/v1/ai/knowledge-bases/{root.Id}/members",actor,new SetAiKnowledgeMembersRequest([other.UserId],root.Version));
                Assert.AreEqual(0,(await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path,otherToken)).Items.Count);
                Assert.AreEqual(0,(await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path,admin)).Items.Count);
                if(bridge is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(bridge,"ready.json"),JsonSerializer.Serialize(new{username=name,password=FullNetApiFactory.TestPassword,knowledgeBaseId=root.Id,documentId=docs[0]}));
                    await Wait("advance.json");Assert.AreEqual(1,await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0,Version=Version+1 WHERE Id=@Id AND TenantId IS NULL AND OwnerUserId=@Owner",new{Id=root.Id,Owner=curator.UserId}));
                    await File.WriteAllTextAsync(Path.Combine(bridge,"changed.json"),"{}");await Wait("complete.json");
                    // 页面可能在调试时停留较久，后续租户场景通过正式重新登录获得新令牌。
                    admin=await NativeApiE2EAssertions.LoginAsync(client,api?.LogFilePath??"K05 JIT");
                }
            }
            else
            {
                var hostOnly=await Send<AiKnowledgeOperationsPage>(HttpMethod.Get,path,otherToken);Assert.AreEqual(0,hostOnly.Items.Count);
            }
            var paid=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");Assert.AreEqual(0,paid);
            Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        }

        async Task SeedVersion(Guid document,Guid version,Guid library,string state,DateTimeOffset time,Guid? faqRevision=null)
        {
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document (Id,KnowledgeBaseId,Title,Description,IsDeleted,CreatedAtUtc,UpdatedAtUtc,Version) VALUES (@Id,@Library,'PRIVATE_DOCUMENT_TITLE',NULL,0,@Now,NULL,1)",new{Id=document,Library=library,Now=time});
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_version (Id,DocumentId,FileId,FaqRevisionId,VersionNumber,OriginalFileName,ContentType,ContentHash,SizeBytes,UploadedByUserId,CreatedAtUtc,ParsingPolicyKey) VALUES (@Id,@Document,@File,@Faq,1,'PRIVATE_FILENAME','text/plain',@Hash,1,@Actor,@Now,'default')",
                new{Id=version,Document=document,File=faqRevision is null?(Guid?)Guid.CreateVersion7():null,Faq=faqRevision,Hash=new string('a',64),Actor=curator.UserId,Now=time});
            var task=Guid.CreateVersion7();await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_index_task (Id,DocumentId,VersionId,DocumentVersion,KnowledgeBaseVersion,ModelConfigId,ModelConfigVersion,StateKey,AttemptCount,FailureCode,LeaseToken,LeaseExpiresAtUtc,CreatedAtUtc,UpdatedAtUtc,Generation) VALUES (@Id,@Document,@Version,1,1,@Model,1,@State,1,@Failure,@Lease,@Expires,@Now,@Now,0)",
                new{Id=task,Document=document,Version=version,Model=Guid.CreateVersion7(),State=state,Failure=state=="failed"?"PRIVATE_EXCEPTION_PAYLOAD":null,Lease=state=="running"?(Guid?)Guid.CreateVersion7():null,Expires=state=="running"?(DateTimeOffset?)time.AddMinutes(-1):null,Now=time});
        }
        async Task SeedPublication(Guid document,Guid version,Guid generation,DateTimeOffset time,DateTimeOffset from,DateTimeOffset until)
        {
            var id=Guid.CreateVersion7();await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_publication (Id,DocumentId,GenerationId,VersionId,RequestKey,ActorUserId,SessionId,SessionKind,SecurityStamp,ActorScope,EffectiveScope,SnapshotJson,SnapshotHash,StateKey,EffectiveFromUtc,EffectiveToUtc,ExpectedHeadVersion,DocumentVersion,KnowledgeBaseVersion,CreatedAtUtc,UpdatedAtUtc,Version) VALUES (@Id,@Doc,@Generation,@Version,@Request,@Actor,@Session,'refresh','fixture','host','host','{}',@Hash,'published',@From,@Until,0,1,1,@Now,@Now,1)",
                new{Id=id,Doc=document,Generation=generation,Version=version,Request=Guid.CreateVersion7(),Actor=curator.UserId,Session=Guid.CreateVersion7(),Hash=new string('b',64),From=from,Until=until,Now=time});
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_publication_head (Id,PublicationId,Version,UpdatedAtUtc) VALUES (@Id,@Publication,1,@Now)",new{Id=document,Publication=id,Now=time});
        }
        async Task<T> Send<T>(HttpMethod method,string path,string token,object? body=null,HttpStatusCode expected=HttpStatusCode.OK)
        {
            using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);if(body is not null)request.Content=JsonContent.Create(body,body.GetType());
            using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,response.StatusCode,$"{method} {path}: {text}");
            if(path.StartsWith("/api/v1/ai/knowledge-operations",StringComparison.Ordinal)){Assert.AreEqual("no-store",response.Headers.CacheControl?.ToString());Assert.IsFalse(text.Contains("PRIVATE_",StringComparison.Ordinal));}
            return JsonSerializer.Deserialize<T>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        async Task Expect(string path,string token,HttpStatusCode expected)
        {
            var titles=new List<string>();
            foreach(var language in expected==HttpStatusCode.BadRequest?new[]{"zh-CN","en-US"}:new[]{"zh-CN"})
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Authorization=new("Bearer",token);request.Headers.AcceptLanguage.ParseAdd(language);
                using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,response.StatusCode,text);
                if(expected==HttpStatusCode.BadRequest)
                {
                    Assert.AreEqual("application/problem+json",response.Content.Headers.ContentType?.MediaType);
                    using var problem=JsonDocument.Parse(text);Assert.AreEqual(AiKnowledgeErrorCodes.InputInvalid,problem.RootElement.GetProperty("code").GetString());
                    Assert.AreEqual("https://full.net/errors/"+AiKnowledgeErrorCodes.InputInvalid,problem.RootElement.GetProperty("type").GetString());
                    var title=problem.RootElement.GetProperty("title").GetString();Assert.IsFalse(string.IsNullOrWhiteSpace(title));titles.Add(title!);
                    Assert.IsTrue(response.Content.Headers.ContentLanguage.Contains(language));
                }
            }
            if(titles.Count==2)Assert.AreNotEqual(titles[0],titles[1],"错误标题必须按请求语言映射，不能固定英语或回显输入。");
        }
        async Task Wait(string file)
        {using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(10));while(!File.Exists(Path.Combine(bridge!,file))){deadline.Token.ThrowIfCancellationRequested();await Task.Delay(250,deadline.Token);}}
    }
}
