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
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>实际 HTTP 持久化、冻结和独立批准；本场景不把独立样例冒充私人反馈或实际模型评估。</summary>
internal static class KnowledgeEvaluationSetScenario
{
    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp = Guid.CreateVersion7().ToString("N");
        var settings = new Dictionary<string,string?>
        {
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(),"fullnet-k05-evaluation-files",stamp),
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(),"fullnet-k05-evaluation-keys",stamp)
        };
        var bridge = native && provider == DatabaseProvider.MySql ? Environment.GetEnvironmentVariable("FULLNET_K05_EVALUATION_BROWSER_BRIDGE") : null;
        if (bridge is not null) { Directory.CreateDirectory(bridge); settings["Identity:AllowedOrigins:1"]="http://localhost:34492"; settings["Tenancy:HostDomains:1"]="127.0.0.1"; }
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer,connection);
        if (Environment.GetEnvironmentVariable("FULLNET_K05_EVALUATION_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new { provider=provider.ToString(),database=db.Database,native,
                filesRoot=settings["Files:Local:RootPath"],keysRoot=settings["DataProtection:KeyRingPath"] })+"\n");
        using var factory = new FullNetApiFactory(provider,connection,settingsOverrides:settings); await factory.InitializeAsync();
        var permissions = new[] {"identity.navigation.read","platform.dashboard.read",AiKnowledgePermissions.Create,AiKnowledgePermissions.Read,AiKnowledgePermissions.Update,AiKnowledgePermissions.MembersUpdate,
            AiKnowledgeEvaluationPermissions.Read,AiKnowledgeEvaluationPermissions.Create,AiKnowledgeEvaluationPermissions.Append,AiKnowledgeEvaluationPermissions.Freeze,
            AiKnowledgeEvaluationPermissions.Restore,AiKnowledgeEvaluationPermissions.Copy,AiKnowledgeEvaluationPermissions.Approve,AiKnowledgeEvaluationPermissions.Withdraw,AiKnowledgeEvaluationPermissions.Feedback};
        var ownerName = "evaluation-owner-"+stamp; var reviewerName="evaluation-reviewer-"+stamp; var readerName="evaluation-no-read-"+stamp;
        var owner = await factory.CreateHostIdentityAsync(ownerName,permissions,password:FullNetApiFactory.TestPassword);
        var reviewer = await factory.CreateHostIdentityAsync(reviewerName,permissions,password:FullNetApiFactory.TestPassword);
        await factory.CreateHostIdentityAsync(readerName,permissions.Where(code=>code!=AiKnowledgeEvaluationPermissions.Read).ToArray(),password:FullNetApiFactory.TestPassword);
        var artifact = native ? NativeApiArtifactLocator.RequireArtifact() : null;
        await using var api = artifact is null ? null : await NativeApiProcessHost.StartAsync(artifact,provider,connection,settings,NativeAotTestTimeouts.ProcessStartup,
            listenAddress:bridge is null ? null : new Uri("http://0.0.0.0:64215/"));
        using var client = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        using var ownerClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        using var reviewerClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        using var readerClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        var admin = await NativeApiE2EAssertions.LoginAsync(client,api?.LogFilePath ?? "K05 evaluation JIT");
        var actor = await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient,ownerName);
        var hostActor = actor;
        var peer = await IntegrationTestAuthHelper.LoginAsHostUserAsync(reviewerClient,reviewerName);
        var reader = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient,readerName);
        await db.OpenAsync();
        if (!native && Environment.GetEnvironmentVariable("FULLNET_K05_EVALUATION_OPENAPI_EXPORT") is {Length:>0} export)
        {
            Directory.CreateDirectory(export); using var response=await client.GetAsync("/openapi/v1.json"); response.EnsureSuccessStatusCode();
            await File.WriteAllTextAsync(Path.Combine(export,"evaluation-"+provider.ToString().ToLowerInvariant()+".json"),await response.Content.ReadAsStringAsync());
        }
        foreach (var scoped in new[] {false,true})
        {
            Guid? tenant = null;
            if (scoped)
            {
                // 页面等待可能消耗原管理员访问期限，准备租户阶段前通过正式登录重新取得令牌。
                admin = await NativeApiE2EAssertions.LoginAsync(client,api?.LogFilePath ?? "K05 evaluation JIT");
                hostActor = await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient,ownerName);
                tenant = (await Send<TenantContextSummary[]>(HttpMethod.Get,"/api/v1/tenancy/available",admin)).Single(value=>value.Identifier=="acme").Id;
                actor = (await Send<TenantContextTokenResponse>(HttpMethod.Put,"/api/v1/tenancy/context",admin,new ChangeTenantContextRequest(tenant))).AccessToken;
            }
            var root = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-bases",actor,new CreateAiKnowledgeBaseRequest("K05 独立评估验收",null),HttpStatusCode.Created);
            var path = $"/api/v1/ai/knowledge-bases/{root.Id}/evaluation-sets";
            if (!scoped)
            {
                await Status(HttpMethod.Get,path,reader,null,HttpStatusCode.Forbidden);
                await Send<AiKnowledgeMembersResponse>(HttpMethod.Put,$"/api/v1/ai/knowledge-bases/{root.Id}/members",actor,new SetAiKnowledgeMembersRequest([reviewer.UserId],root.Version));
            }
            var sample = new AiFeedbackEvaluationSample("zh-CN","人工脱敏问题","人工参考答案","独立人工证据",false);
            var create = new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(),null,0,"K05 原始修订",[new(null,sample,"独立人工编写",null)],true,false);
            var original = await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,path,actor,create);
            Assert.AreEqual("available",original.DeliveryState); Assert.IsNotNull(original.Workspace);
            var current=original.Workspace; var genesis=current.Revision.Id;
            var replay=await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,path,actor,create);
            Assert.IsTrue(replay.Replayed); Assert.AreEqual(original.SavedRevisionId,replay.SavedRevisionId);
            var itemPath=path+"/"+current.Id;
            await Status(HttpMethod.Post,path,actor,create with { Name="碰撞输入" },HttpStatusCode.Conflict);
            await Status(HttpMethod.Post,path,actor,create with { RequestKey=Guid.CreateVersion7(),ConfirmManualRedaction=false },HttpStatusCode.BadRequest);
            await Status(HttpMethod.Post,path,actor,create with { RequestKey=Guid.CreateVersion7(),Cases=[new(null,sample,null,"old-purpose-candidate")] },HttpStatusCode.Conflict);
            var append = new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(),current.Revision.Id,current.Version,"K05 冻结修订",
                current.Revision.Cases.Select(value=>new AiKnowledgeEvaluationCaseInput(value.Id,value.Sample,value.IndependentBasis,null)).ToArray(),true,true);
            current = (await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Put,itemPath,actor,append)).Workspace!;
            Assert.AreEqual("frozen",current.Revision.State); Assert.IsFalse(current.CanIndependentlyApprove); Assert.AreEqual(2,current.Revision.Number);
            Assert.AreEqual("K05 原始修订",(await Send<AiKnowledgeEvaluationWorkspace>(HttpMethod.Get,itemPath+"?revisionId="+genesis,actor)).Revision.Name);
            await Status(HttpMethod.Post,itemPath+"/approvals",actor,new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(),current.Revision.Id,current.Version,"approved",true),HttpStatusCode.Conflict);
            await Status(HttpMethod.Put,itemPath,actor,append with { RequestKey=Guid.CreateVersion7() },HttpStatusCode.Conflict);
            if (!scoped)
            {
                var read=await Send<AiKnowledgeEvaluationWorkspace>(HttpMethod.Get,itemPath,peer); Assert.IsTrue(read.CanIndependentlyApprove);
                await Status(HttpMethod.Put,itemPath,peer,append with { RequestKey=Guid.CreateVersion7(),ExpectedVersion=current.Version,ExpectedRevisionId=current.Revision.Id },HttpStatusCode.NotFound);
                var review=new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(),current.Revision.Id,current.Version,"approved",true);
                current=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,itemPath+"/approvals",peer,review)).Workspace!;
                Assert.AreEqual("approved",current.ApprovalState);
                Assert.IsTrue((await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,itemPath+"/approvals",peer,review)).Replayed);
                current=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,itemPath+"/withdrawals",actor,new ReviewAiKnowledgeEvaluationSet(Guid.CreateVersion7(),current.Revision.Id,current.Version,"withdrawn",false))).Workspace!;
                Assert.AreEqual("withdrawn",current.ApprovalState);
                Assert.AreEqual(2,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_evaluation_approval WHERE SetId=@Id",new { Id=current.Id }));
            }
            var copy=(await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,itemPath+"/copy",actor,new CopyAiKnowledgeEvaluationSet(Guid.CreateVersion7(),current.Revision.Id,current.Version))).Workspace!;
            Assert.AreNotEqual(current.Id,copy.Id); Assert.AreEqual("draft",copy.Revision.State); Assert.AreEqual("none",copy.ApprovalState);
            var restored=await Send<AiKnowledgeEvaluationMutation>(HttpMethod.Post,itemPath+"/restore",actor,new RestoreAiKnowledgeEvaluationSet(Guid.CreateVersion7(),current.Revision.Id,current.Version,genesis));
            Assert.IsNotNull(restored.Workspace); Assert.AreEqual(genesis,restored.Workspace.Revision.RestoredFromRevisionId); Assert.AreEqual("draft",restored.Workspace.Revision.State);
            Assert.AreEqual("none",restored.Workspace.ApprovalState);
            var locked = await VerifyRootWait(restored.Workspace,false);
            await VerifyRootWait(locked,true);
            var list=await Send<AiKnowledgeEvaluationPage>(HttpMethod.Get,path,actor); Assert.AreEqual(2,list.Items.Count); Assert.IsNull(list.NextAfterId);
            await Status(HttpMethod.Get,path+"?tenantId="+Guid.CreateVersion7(),actor,null,HttpStatusCode.BadRequest);
            if (scoped) await Status(HttpMethod.Get,path,hostActor,null,HttpStatusCode.NotFound);
            if (!scoped && bridge is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(bridge,"ready.json"),JsonSerializer.Serialize(new { username=ownerName,reviewer=reviewerName,password=FullNetApiFactory.TestPassword,knowledgeBaseId=root.Id }));
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
                while(!File.Exists(Path.Combine(bridge,"advance.json"))){await Task.Delay(250,deadline.Token);}
                actor=await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient,ownerName);
                root=await Send<AiKnowledgeBaseResponse>(HttpMethod.Get,$"/api/v1/ai/knowledge-bases/{root.Id}",actor);
                await Send<AiKnowledgeBaseResponse>(HttpMethod.Put,$"/api/v1/ai/knowledge-bases/{root.Id}",actor,
                    new UpdateAiKnowledgeBaseRequest(root.Name,root.Description,false,root.Version));
                await File.WriteAllTextAsync(Path.Combine(bridge,"changed.json"),"{}");
                while(!File.Exists(Path.Combine(bridge,"complete.json"))){await Task.Delay(250,deadline.Token);}
            }

            async Task<AiKnowledgeEvaluationWorkspace> VerifyRootWait(AiKnowledgeEvaluationWorkspace original,bool cancel)
            {
                await using var holder=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,connection);await holder.OpenAsync();
                await using var held=await holder.BeginTransactionAsync();
                var lockSql=provider==DatabaseProvider.SqlServer?"SELECT Id FROM fn_ai_knowledge_base WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id"
                    :"SELECT Id FROM fn_ai_knowledge_base WHERE Id=@Id FOR UPDATE";
                Assert.AreEqual(root.Id,await holder.QuerySingleAsync<Guid>(lockSql,new{Id=root.Id},held));
                var holderId=await holder.QuerySingleAsync<long>(provider==DatabaseProvider.SqlServer?"SELECT @@SPID":"SELECT CONNECTION_ID()",transaction:held);
                var observerConnection=provider==DatabaseProvider.MySql
                    ?new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()){Database=db.Database}.ConnectionString:connection;
                await using var observer=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,observerConnection);
                var body=new SaveAiKnowledgeEvaluationSet(Guid.CreateVersion7(),original.Revision.Id,original.Version,"K05 根锁后修订",
                    original.Revision.Cases.Select(value=>new AiKnowledgeEvaluationCaseInput(value.Id,value.Sample,value.IndependentBasis,null)).ToArray(),true,false);
                using var request=new HttpRequestMessage(HttpMethod.Put,path+"/"+original.Id){Content=JsonContent.Create(body)};
                request.Headers.Authorization=new("Bearer",actor);using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var pending=client.SendAsync(request,cancellation.Token);
                var waitSql=provider==DatabaseProvider.SqlServer
                    ?"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id=@Holder AND wait_type LIKE 'LCK_M_%'"
                    :"SELECT COUNT(*) FROM performance_schema.data_lock_waits AS waiting INNER JOIN performance_schema.data_locks AS blocking ON blocking.ENGINE_LOCK_ID=waiting.BLOCKING_ENGINE_LOCK_ID WHERE blocking.OBJECT_SCHEMA=DATABASE() AND blocking.OBJECT_NAME='fn_ai_knowledge_base' AND blocking.THREAD_ID=(SELECT THREAD_ID FROM performance_schema.threads WHERE PROCESSLIST_ID=@Holder)";
                var until=DateTime.UtcNow.AddSeconds(10);var observed=false;
                while(DateTime.UtcNow<until){if(await observer.QuerySingleAsync<int>(waitSql,new{Holder=holderId})>0){observed=true;break;}await Task.Delay(50);}
                Assert.IsTrue(observed,"必须观察当前专用数据库真实根锁等待，不能只凭延迟推测。");Assert.IsFalse(pending.IsCompleted);
                if(cancel)
                {
                    cancellation.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>{using var response=await pending;});
                    until=DateTime.UtcNow.AddSeconds(10);
                    while(DateTime.UtcNow<until&&await observer.QuerySingleAsync<int>(waitSql,new{Holder=holderId})>0)await Task.Delay(50);
                    Assert.AreEqual(0,await observer.QuerySingleAsync<int>(waitSql,new{Holder=holderId}),"取消后等待事务必须排空。");
                    await held.RollbackAsync();
                    var reloaded=await Send<AiKnowledgeEvaluationWorkspace>(HttpMethod.Get,path+"/"+original.Id,actor);
                    Assert.AreEqual(original.Version,reloaded.Version);Assert.AreEqual(original.Revision.Id,reloaded.Revision.Id);
                    Console.WriteLine($"K05 evaluation root wait drained with zero revision write: provider={provider},tenant={scoped},native={native},cancel={cancel}");
                    return reloaded;
                }
                await held.CommitAsync();using var completed=await pending;var text=await completed.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.OK,completed.StatusCode,text);var saved=JsonSerializer.Deserialize<AiKnowledgeEvaluationMutation>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                Assert.IsNotNull(saved.Workspace);Assert.AreEqual(original.Version+1,saved.Workspace.Version);
                Console.WriteLine($"K05 evaluation root wait verified: provider={provider},tenant={scoped},native={native},cancel={cancel}");
                return saved.Workspace;
            }
        }
        Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));

        async Task<T> Send<T>(HttpMethod method,string path,string bearer,object? body=null,HttpStatusCode expected=HttpStatusCode.OK)
        {
            using var request=new HttpRequestMessage(method,path); request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
            if(body is not null)request.Content=JsonContent.Create(body,body.GetType());
            using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,response.StatusCode,$"{method} {path}: {text}");
            return JsonSerializer.Deserialize<T>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }
        async Task Status(HttpMethod method,string path,string bearer,object? body,HttpStatusCode expected)
        {
            using var request=new HttpRequestMessage(method,path); request.Headers.Authorization=new("Bearer",bearer);if(body is not null)request.Content=JsonContent.Create(body,body.GetType());
            using var response=await client.SendAsync(request);Assert.AreEqual(expected,response.StatusCode,await response.Content.ReadAsStringAsync());
            Assert.AreEqual("application/problem+json",response.Content.Headers.ContentType?.MediaType);
        }
    }
}
