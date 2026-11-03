using System.Data.Common;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.CompletePersonalSchedules;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实静态Run、独立批准和Calendar接收方；仅故障包装器丢弃已提交回执，不替换业务规则。</summary>
internal static class PersonalScheduleCompletionScenario
{
    internal static async Task VerifyAsync(DatabaseProvider provider, bool native)
    {
        var connection=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp=Guid.CreateVersion7().ToString("N");var settings=AiAgentRuntimeTestSettings.ForIntegrationTests();
        settings["DataProtection:KeyRingPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k06-completion-keys",stamp);
        settings["Files:Local:RootPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k06-completion-files",stamp);
        var faults=new Faults();var noModel=new NoModelFactory();
        using var factory=new FullNetApiFactory(provider,connection,settings,configureTestServices:services=>
        {
            services.RemoveAll<IAiModelClientFactory>();services.AddSingleton<IAiModelClientFactory>(noModel);
            services.AddScoped<AiToolAuthorizationPort>();services.AddScoped<AiBackgroundToolAuthorizationPort>();
            services.AddScoped<AiToolAuditPort>();services.AddScoped<AiBackgroundToolAuditPort>();
            services.Replace(ServiceDescriptor.Scoped<IToolAuthorizationPort>(sp=>new MixedAuthorization(sp.GetRequiredService<IAgentRunExecutionContext>(),sp.GetRequiredService<AiToolAuthorizationPort>(),sp.GetRequiredService<AiBackgroundToolAuthorizationPort>())));
            services.Replace(ServiceDescriptor.Scoped<IToolAuditPort>(sp=>new MixedAudit(sp.GetRequiredService<IAgentRunExecutionContext>(),sp.GetRequiredService<AiToolAuditPort>(),sp.GetRequiredService<AiBackgroundToolAuditPort>(),faults)));
            services.AddScoped<Full.NET.Modules.Calendar.Features.Completion.PersonalScheduleCompletion>();
            services.Replace(ServiceDescriptor.Scoped<IPersonalScheduleCompletion>(sp=>new LostReceipt(sp.GetRequiredService<Full.NET.Modules.Calendar.Features.Completion.PersonalScheduleCompletion>(),faults)));
        });
        await factory.InitializeAsync();
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,connection);await db.OpenAsync();
        if(Environment.GetEnvironmentVariable("FULLNET_K06_COMPLETION_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=provider.ToString(),database=db.Database,native,filesRoot=settings["Files:Local:RootPath"],keysRoot=settings["DataProtection:KeyRingPath"]})+"\n");
        string[] rights=[CalendarPermissions.Read,CalendarPermissions.Create,CalendarPermissions.Update,CalendarPermissions.SetStatus,PersonalScheduleCompletionKeys.Permission,
            AiModelPermissions.Read,AiAgentRunPermissions.Read,AiAgentRunPermissions.Create,AiAgentRunPermissions.Resume,AiAgentRunPermissions.Cancel,
            AiAgentApprovalPermissions.Read,AiAgentApprovalPermissions.Request,AiAgentApprovalPermissions.Decide,"tenancy.tenants.switch"];
        var ownerName="completion-owner-"+stamp;var reviewerName="completion-reviewer-"+stamp;
        var ownerIdentity=await factory.CreateHostIdentityAsync(ownerName,rights,password:FullNetApiFactory.TestPassword);
        var reviewerIdentity=await factory.CreateHostIdentityAsync(reviewerName,rights,password:FullNetApiFactory.TestPassword);
        await factory.EnsureHostUserProfileEmailAsync(ownerIdentity.UserId,ownerName+"@example.test",ownerName);
        await factory.EnsureHostUserProfileEmailAsync(reviewerIdentity.UserId,reviewerName+"@example.test",reviewerName);
        await using var api=native?await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(),provider,connection,settings,NativeAotTestTimeouts.ProcessStartup):null;
        using var client=api?.CreateClient()??factory.CreateClientForHost("localhost");
        using var ownerClient=api?.CreateClient()??factory.CreateClientForHost("localhost");
        using var reviewerClient=api?.CreateClient()??factory.CreateClientForHost("localhost");
        // 契约导出与业务断言独立，后续业务失败不能被该结构快照冒充通过。
        if(!native&&Environment.GetEnvironmentVariable("FULLNET_K06_COMPLETION_OPENAPI_EXPORT_DIR") is {Length:>0} export)
        {Directory.CreateDirectory(export);using var response=await client.GetAsync("/openapi/v1.json");Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);await File.WriteAllTextAsync(Path.Combine(export,provider.ToString()+".json"),await response.Content.ReadAsStringAsync());}
        var administrator=await NativeApiE2EAssertions.LoginAsync(client,api?.LogFilePath??"K06 completion HTTP");
        var owner=await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient,ownerName);
        var reviewer=await IntegrationTestAuthHelper.LoginAsHostUserAsync(reviewerClient,reviewerName);
        var model=await Send<AiModelConfigResponse>(HttpMethod.Post,"/api/v1/ai/model-configs",administrator,
            new CreateAiModelConfigRequest(null,"静态完成图配置元数据","ollama","https://provider.test","model",null,null,false,true),HttpStatusCode.Created);
        var tenant=(await Send<TenantContextSummary[]>(HttpMethod.Get,"/api/v1/tenancy/available",administrator)).Single(t=>t.Identifier=="acme");
        foreach(var scoped in new[]{false,true})
        {
            if(scoped)
            {
                var tenantAdministrator=await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client,administrator,tenant.Id);
                foreach(var pair in new[]{(name:ownerName,token:owner),(name:reviewerName,token:reviewer)})
                {
                    var invitation=await Send<CreateTenantInvitationResult>(HttpMethod.Post,"/api/v1/identity/tenant-members/invitations",tenantAdministrator,
                        new CreateTenantInvitationRequest(pair.name+"@example.test",TenantMemberRoles.Admin));
                    await Send<AcceptTenantInvitationResponse>(HttpMethod.Post,"/api/v1/identity/tenant-invitations/accept",pair.token,new AcceptTenantInvitationRequest(invitation.InvitationToken));
                }
                owner=await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient,ownerName);
                reviewer=await IntegrationTestAuthHelper.LoginAsHostUserAsync(reviewerClient,reviewerName);
                owner=await IntegrationTestTenantContextHelper.SwitchToTenantAsync(ownerClient,owner,tenant.Id);
                reviewer=await IntegrationTestTenantContextHelper.SwitchToTenantAsync(reviewerClient,reviewer,tenant.Id);
            }
            Guid? tenantId=scoped?tenant.Id:null;
            await AiAgentRunApiAssertions.SeedFreshWorkerHeartbeatAsync(factory);
            var beforeFiles=await Count("fn_files_file");var beforeReceipt=await Count("fn_calendar_schedule_completion");
            var normal=await Prepare();
            if(scoped)
                await Exact(HttpMethod.Post,"/api/v1/ai/agent/approvals",owner,
                    new CreateAiAgentApprovalRequest(normal.Run,Guid.CreateVersion7(),"ai.chat.sessions.rename",1,"{}"),HttpStatusCode.Forbidden);
            Assert.AreEqual("pending",(await Schedule(normal.Schedule.Id)).Status);
            Assert.AreEqual(0,await CountIntent(normal.Run));
            await Exact(HttpMethod.Post,$"/api/v1/ai/agent/approvals/{normal.Approval.Id}/decide",owner,new DecideAiAgentApprovalRequest(true,normal.Approval.Version),HttpStatusCode.Forbidden);
            await Send<AiAgentApprovalResponse>(HttpMethod.Post,$"/api/v1/ai/agent/approvals/{normal.Approval.Id}/decide",reviewer,new DecideAiAgentApprovalRequest(true,normal.Approval.Version));
            NativeWorkerProcessHost? worker=null;
            try
            {
                if(native)
                {
                    if(!NativeWorkerArtifactLocator.TryResolve(out var artifact,out var reason))throw new InvalidOperationException(reason);
                    worker=await NativeWorkerProcessHost.StartAsync(artifact,provider,connection,NativeAotTestTimeouts.ProcessStartup,
                        additionalSettings:new Dictionary<string,string?>(settings){["FullNet:Ai:AgentRuntime:PollMilliseconds"]="1000"});
                }
                await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{normal.Run}/resume",owner);
                if(!native){await Tick();await Deliver(normal.Run);}
                var actual=await WaitResult(normal.Run,"completed");
                Assert.AreEqual(normal.Schedule.Version+1,actual.ActualVersion);Assert.IsNotNull(actual.CompletedAtUtc);
                var schedule=await Schedule(normal.Schedule.Id);Assert.AreEqual("completed",schedule.Status);Assert.AreEqual(normal.Schedule.Content,schedule.Content);
                Assert.AreEqual(normal.Schedule.StartAtUtc,schedule.StartAtUtc);Assert.AreEqual(normal.Schedule.EndAtUtc,schedule.EndAtUtc);
                Assert.AreEqual(1,await CountIntent(normal.Run));
                var stored=await StoredRequest(normal.Run);var receipt=await CalendarReceipt(stored);
                Assert.IsNotNull(receipt);Assert.IsTrue(receipt.IsFor(stored));
                await ReplayMessage(normal.Run);await ReplayMessage(normal.Run);
                Assert.AreEqual(schedule.Version,(await Schedule(normal.Schedule.Id)).Version);
                var reconciled=await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Post,StatusPath(normal.Run)+"/reconcile",owner);
                Assert.AreEqual(actual.OperationId,reconciled.OperationId); Assert.AreEqual("completed",reconciled.State);
                await InScope(async sp=>await Assert.ThrowsAsync<InvalidOperationException>(()=>sp.GetRequiredService<IPersonalScheduleCompletion>().ReceiveAsync(stored with{ExpectedVersion=stored.ExpectedVersion+1})));
            }
            finally{if(worker is not null)await worker.DisposeAsync();}

            // Native两种范围先完成真实Worker正常链路；故障补充放在最后的Tenant范围，避免
            // 下一次Worker启动把夹具刻意保留的unknown Outbox误计为本次正常链路失败。
            if(native&&!scoped)
            {
                Assert.AreEqual(beforeFiles,await Count("fn_files_file"));Assert.AreEqual(beforeReceipt+1,await Count("fn_calendar_schedule_completion"));
                Assert.AreEqual(0,await Count("fn_ai_operation_budget"));continue;
            }

            // 提交后丢发送方回执：真实批准已消费与意图已落库，原工具必须进入对账而不是再次执行。
            var senderLost=await PrepareAndApprove();faults.LoseSenderReply=true;
            await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{senderLost.Run}/resume",owner);await Tick();
            Assert.AreEqual("reconciliation_required",(await Send<AiAgentRunResponse>(HttpMethod.Get,$"/api/v1/ai/agent/runs/{senderLost.Run}",owner)).StatusKey);
            Assert.AreEqual("pending",(await Schedule(senderLost.Schedule.Id)).Status);Assert.AreEqual(1,await CountIntent(senderLost.Run));
            await Deliver(senderLost.Run);Assert.AreEqual("completed",(await WaitResult(senderLost.Run,"completed")).State);

            // 接收方提交后丢响应：第二次只查准确回执，不再调用Receive或根据资源状态猜测。
            var lost=await PrepareAndApprove();await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{lost.Run}/resume",owner);await Tick();
            faults.LoseReceiverReply=true;var calls=faults.ReceiveCalls;
            await Assert.ThrowsAsync<IOException>(()=>Deliver(lost.Run));
            var unknown=await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Get,StatusPath(lost.Run),owner);Assert.AreEqual("unknown",unknown.State);
            var recovered=await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Post,StatusPath(lost.Run)+"/reconcile",owner);
            Assert.AreEqual("completed",recovered.State);Assert.AreEqual(calls+1,faults.ReceiveCalls);
            await Deliver(lost.Run);Assert.AreEqual(calls+1,faults.ReceiveCalls);Assert.AreEqual(lost.Schedule.Version+1,(await Schedule(lost.Schedule.Id)).Version);

            // 派发标记写入失败时根本不调用Calendar；仍沿同一accepted原操作处理，不创建替代操作。
            var blocked=await PrepareAndApprove();await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{blocked.Run}/resume",owner);await Tick();
            var beforeDispatch=faults.ReceiveCalls;
            await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?
                "CREATE TRIGGER demo_ai_completion_fail ON fn_ai_schedule_completion_intent AFTER UPDATE AS THROW 50000,'dispatch marker rollback',1;":
                "CREATE TRIGGER demo_ai_completion_fail BEFORE UPDATE ON fn_ai_schedule_completion_intent FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='dispatch marker rollback'");
            try{var failure=await Assert.ThrowsAsync<DbException>(()=>Deliver(blocked.Run));StringAssert.Contains(failure.Message,"dispatch marker rollback");}
            finally{await db.ExecuteAsync("DROP TRIGGER demo_ai_completion_fail");}
            Assert.AreEqual(beforeDispatch,faults.ReceiveCalls);Assert.AreEqual("pending",(await Schedule(blocked.Schedule.Id)).Status);
            Assert.AreEqual("accepted",(await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Get,StatusPath(blocked.Run),owner)).State);
            await Deliver(blocked.Run);Assert.AreEqual("completed",(await WaitResult(blocked.Run,"completed")).State);

            // Calendar已提交但AI结果投影失败，事先提交的unknown标记必须仍在，只能查准确回执。
            var projection=await PrepareAndApprove();await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{projection.Run}/resume",owner);await Tick();
            await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?
                "CREATE TRIGGER demo_ai_completion_fail ON fn_ai_schedule_completion_intent AFTER UPDATE AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Outcome='completed') BEGIN ;THROW 50000,'projection rollback',1; END END":
                "CREATE TRIGGER demo_ai_completion_fail BEFORE UPDATE ON fn_ai_schedule_completion_intent FOR EACH ROW BEGIN IF NEW.Outcome='completed' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='projection rollback'; END IF; END");
            try{var failure=await Assert.ThrowsAsync<DbException>(()=>Deliver(projection.Run));StringAssert.Contains(failure.Message,"projection rollback");}
            finally{await db.ExecuteAsync("DROP TRIGGER demo_ai_completion_fail");}
            Assert.AreEqual("unknown",(await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Get,StatusPath(projection.Run),owner)).State);
            Assert.AreEqual("completed",(await Schedule(projection.Schedule.Id)).Status);
            var beforeReconcile=faults.ReceiveCalls;
            Assert.AreEqual("completed",(await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Post,StatusPath(projection.Run)+"/reconcile",owner)).State);
            Assert.AreEqual(beforeReconcile,faults.ReceiveCalls);

            // 真实数据库触发器拒绝回执INSERT，Calendar状态修改必须同事务回滚；未知且无回执不能重发。
            var rollback=await PrepareAndApprove();await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{rollback.Run}/resume",owner);await Tick();
            await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?
                "CREATE TRIGGER demo_calendar_completion_fail ON fn_calendar_schedule_completion INSTEAD OF INSERT AS THROW 50000,'receipt rollback',1;":
                "CREATE TRIGGER demo_calendar_completion_fail BEFORE INSERT ON fn_calendar_schedule_completion FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='receipt rollback'");
            try
            {
                var failure = await Assert.ThrowsAsync<DbException>(()=>Deliver(rollback.Run));
                StringAssert.Contains(failure.Message, "receipt rollback");
            }
            finally{await db.ExecuteAsync("DROP TRIGGER demo_calendar_completion_fail");}
            Assert.AreEqual("pending",(await Schedule(rollback.Schedule.Id)).Status);Assert.IsNull(await CalendarReceipt(await StoredRequest(rollback.Run)));
            var none=await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Post,StatusPath(rollback.Run)+"/reconcile",owner);Assert.AreEqual("unknown",none.State);
            var receives=faults.ReceiveCalls;await Assert.ThrowsAsync<InvalidOperationException>(()=>Deliver(rollback.Run));Assert.AreEqual(receives,faults.ReceiveCalls);
            var priorRuns=await Count("fn_ai_agent_run");
            var replacement=new CreateAiAgentRunRequest(Guid.CreateVersion7(),"fullnet-personal-schedule-complete-v1",model.Id,"",1,1,
                PersonalScheduleTarget:new(rollback.Schedule.Id,rollback.Schedule.Version));
            await Exact(HttpMethod.Post,"/api/v1/ai/agent/runs",owner,replacement,HttpStatusCode.Conflict);
            Assert.AreEqual(priorRuns,await Count("fn_ai_agent_run")); Assert.AreEqual(receives,faults.ReceiveCalls);
            var originalClientKey=await db.QuerySingleAsync<Guid>("SELECT ClientRequestId FROM fn_ai_agent_run WHERE Id=@Id",new{Id=rollback.Run});
            var replayed=await Send<CreateAiAgentRunResponse>(HttpMethod.Post,"/api/v1/ai/agent/runs",owner,
                replacement with{ClientRequestId=originalClientKey},HttpStatusCode.Accepted);
            Assert.AreEqual(rollback.Run,replayed.RunId);

            // 批准不允许重绑定到后来版本，恢复前即拒绝，不能创建新意图掩盖CAS冲突。
            var stale=await PrepareAndApprove();
            await Send<PersonalScheduleResponse>(HttpMethod.Put,"/api/v1/calendar/my-personal-schedules/"+stale.Schedule.Id,owner,
                new UpdatePersonalScheduleRequest("后来准确版本",stale.Schedule.StartAtUtc,stale.Schedule.EndAtUtc,stale.Schedule.Version));
            await Exact(HttpMethod.Post,$"/api/v1/ai/agent/runs/{stale.Run}/resume",owner,new{},HttpStatusCode.UnprocessableEntity);
            Assert.AreEqual(0,await CountIntent(stale.Run));
            // 既有Run状态机不允许取消awaiting_approval；不为测试放宽全局状态转换。
            await Exact(HttpMethod.Post,$"/api/v1/ai/agent/runs/{stale.Run}/cancel",owner,new{},HttpStatusCode.UnprocessableEntity);

            // 意图已提交后再次修改目标，由接收方保存准确拒绝，不把当前状态当本操作成功。
            var changed = await PrepareAndApprove();
            await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{changed.Run}/resume",owner); await Tick();
            await Send<PersonalScheduleResponse>(HttpMethod.Put,"/api/v1/calendar/my-personal-schedules/"+changed.Schedule.Id,owner,
                new UpdatePersonalScheduleRequest("接受意图后更新",changed.Schedule.StartAtUtc,changed.Schedule.EndAtUtc,changed.Schedule.Version));
            await Deliver(changed.Run);
            var rejection = await WaitResult(changed.Run,"rejected"); Assert.AreEqual("target_changed",rejection.Reason);
            Assert.IsNull(rejection.ActualVersion); Assert.IsNull(rejection.CompletedAtUtc);

            // 真实根锁等待期间取消，Calendar事务必须排空；unknown没有准确回执时不能重发。
            var cancelled = await PrepareAndApprove();
            await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{cancelled.Run}/resume",owner); await Tick();
            await CancelRootWait(cancelled.Run,cancelled.Schedule.Id);
            Assert.AreEqual("pending",(await Schedule(cancelled.Schedule.Id)).Status);
            Assert.IsNull(await CalendarReceipt(await StoredRequest(cancelled.Run)));
            Assert.AreEqual("unknown",(await Send<AiPersonalScheduleCompletionStatus>(HttpMethod.Post,StatusPath(cancelled.Run)+"/reconcile",owner)).State);

            // 旧原会话在意图提交后失效，新交互会话只能读取原结果，不能替旧会话授予执行权限。
            var revoked = await PrepareAndApprove();
            await Send<bool>(HttpMethod.Post,$"/api/v1/ai/agent/runs/{revoked.Run}/resume",owner); await Tick();
            owner=await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient,ownerName);
            if(scoped) owner=await IntegrationTestTenantContextHelper.SwitchToTenantAsync(ownerClient,owner,tenant.Id);
            var previousReceives=faults.ReceiveCalls;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>Deliver(revoked.Run));
            var denied=await WaitResult(revoked.Run,"unknown"); Assert.IsNull(denied.Reason);
            Assert.AreEqual(previousReceives,faults.ReceiveCalls);
            Assert.IsNull(await CalendarReceipt(await StoredRequest(revoked.Run)));
            Assert.AreEqual("pending",(await Schedule(revoked.Schedule.Id)).Status);

            Assert.AreEqual(beforeFiles,await Count("fn_files_file"));Assert.AreEqual(beforeReceipt+6,await Count("fn_calendar_schedule_completion"));
            Assert.AreEqual(0,noModel.Created);Assert.AreEqual(0,await Count("fn_ai_operation_budget"));

            async Task<(Guid Run,PersonalScheduleResponse Schedule,AiAgentApprovalResponse Approval)> Prepare()
            {
                var now=DateTimeOffset.UtcNow;var target=await Send<PersonalScheduleResponse>(HttpMethod.Post,"/api/v1/calendar/my-personal-schedules",owner,
                    new CreatePersonalScheduleRequest("PRIVATE_COMPLETION_BODY_不进入批准摘要",now,now),HttpStatusCode.Created);
                var request=new CreateAiAgentRunRequest(Guid.CreateVersion7(),"fullnet-personal-schedule-complete-v1",model.Id,"",1,1,
                    PersonalScheduleTarget:new(target.Id,target.Version));
                var runCount=await Count("fn_ai_agent_run");
                await Exact(HttpMethod.Post,"/api/v1/ai/agent/runs",reviewer,request,HttpStatusCode.Forbidden);
                Assert.AreEqual(runCount,await Count("fn_ai_agent_run"));
                var created=await Send<CreateAiAgentRunResponse>(HttpMethod.Post,"/api/v1/ai/agent/runs",owner,request,HttpStatusCode.Accepted);
                Assert.AreEqual(created.RunId,(await Send<CreateAiAgentRunResponse>(HttpMethod.Post,"/api/v1/ai/agent/runs",owner,request,HttpStatusCode.Accepted)).RunId);
                await Tick();var state=await Send<AiAgentRunResponse>(HttpMethod.Get,$"/api/v1/ai/agent/runs/{created.RunId}",owner);Assert.AreEqual("awaiting_approval",state.StatusKey);
                var proposal=await Send<AiPersonalScheduleCompletionProposal>(HttpMethod.Get,StatusPath(created.RunId)+"/proposal",owner);
                var approved=await Send<CreateAiAgentApprovalResponse>(HttpMethod.Post,"/api/v1/ai/agent/approvals",owner,
                    new CreateAiAgentApprovalRequest(created.RunId,proposal.OperationId,proposal.ToolName,proposal.ToolVersion,proposal.ArgumentsJson));
                var summary=await Send<AiAgentApprovalResponse>(HttpMethod.Get,$"/api/v1/ai/agent/approvals/{approved.ApprovalId}",reviewer);
                Assert.IsFalse(JsonSerializer.Serialize(summary).Contains(target.Content,StringComparison.Ordinal));Assert.AreEqual(target.Id.ToString("D"),summary.TargetSummary);
                Assert.AreEqual(FormattableString.Invariant($"pending -> completed; expected version: {target.Version}"),summary.ChangeSummary);
                return(created.RunId,target,summary);
            }
            async Task<(Guid Run,PersonalScheduleResponse Schedule,AiAgentApprovalResponse Approval)> PrepareAndApprove()
            {var value=await Prepare();await Send<AiAgentApprovalResponse>(HttpMethod.Post,$"/api/v1/ai/agent/approvals/{value.Approval.Id}/decide",reviewer,new DecideAiAgentApprovalRequest(true,value.Approval.Version));return value;}
            async Task<PersonalScheduleResponse> Schedule(Guid id)
            {var page=await Send<Full.NET.Abstractions.Results.PagedResult<PersonalScheduleResponse>>(HttpMethod.Get,"/api/v1/calendar/my-personal-schedules?pageSize=200",owner);return page.Items.Single(x=>x.Id==id);}
            Task<int> CountIntent(Guid id)=>db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_schedule_completion_intent WHERE RunId=@Id",new{Id=id});
            async Task<PersonalScheduleCompletionRequest> StoredRequest(Guid id)=>JsonSerializer.Deserialize<PersonalScheduleCompletionRequest>(await db.QuerySingleAsync<string>("SELECT RequestJson FROM fn_ai_schedule_completion_intent WHERE RunId=@Id",new{Id=id}),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            async Task<PersonalScheduleCompletionReceipt?> CalendarReceipt(PersonalScheduleCompletionRequest request)
            {PersonalScheduleCompletionReceipt? found=null;await InScope(async sp=>{found=await sp.GetRequiredService<IPersonalScheduleCompletion>().FindReceiptAsync(request);});return found;}
            async Task InScope(Func<IServiceProvider,Task> action)
            {await using var scope=factory.Services.CreateAsyncScope();var context=scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();if(tenantId is{}id)context.SetTenant(new(id,"acme","Acme"));else context.SetHost();await action(scope.ServiceProvider);}
            async Task Deliver(Guid id,CancellationToken token=default)
            {var operation=await db.QuerySingleAsync<Guid>("SELECT OperationId FROM fn_ai_schedule_completion_intent WHERE RunId=@Id",new{Id=id});await InScope(sp=>sp.GetRequiredService<AiScheduleCompletionService>().DeliverAsync(operation,true,token));}
            async Task CancelRootWait(Guid id,Guid scheduleId)
            {
                var mysql=provider==DatabaseProvider.MySql;
                await using var blocker=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql,connection); await blocker.OpenAsync();
                await using var held=await blocker.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                await blocker.QuerySingleAsync<int>(mysql?"SELECT Version FROM fn_calendar_personal_schedule WHERE Id=@Id FOR UPDATE":
                    "SELECT Version FROM fn_calendar_personal_schedule WITH(UPDLOCK,HOLDLOCK) WHERE Id=@Id",new{Id=scheduleId},held);
                var session=await blocker.QuerySingleAsync<long>(mysql?"SELECT CONNECTION_ID()":"SELECT @@SPID",transaction:held);
                var observerConnection=mysql?new MySqlConnectionStringBuilder(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()){Database=blocker.Database}.ConnectionString:connection;
                await using var observer=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql,observerConnection); await observer.OpenAsync();
                using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var pending=Deliver(id,cancellation.Token);
                try
                {
                    var sql=mysql?"SELECT COUNT(*) FROM performance_schema.data_lock_waits w INNER JOIN performance_schema.data_locks l ON l.ENGINE_LOCK_ID=w.BLOCKING_ENGINE_LOCK_ID INNER JOIN performance_schema.threads h ON h.THREAD_ID=l.THREAD_ID WHERE l.OBJECT_SCHEMA=@Database AND l.OBJECT_NAME='fn_calendar_personal_schedule' AND h.PROCESSLIST_ID=@Session":
                        "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(@Database) AND blocking_session_id=@Session AND wait_type LIKE 'LCK%'";
                    var until=DateTimeOffset.UtcNow.AddSeconds(5); var waiting=false;
                    while(!pending.IsCompleted&&DateTimeOffset.UtcNow<until)
                    {waiting=await observer.QuerySingleAsync<int>(sql,new{Database=blocker.Database,Session=session})>0;if(waiting)break;await Task.Delay(25);}
                    Assert.IsTrue(waiting,"必须从数据库观察到Calendar真实根锁等待。"); await cancellation.CancelAsync();
                    await Assert.ThrowsAsync<OperationCanceledException>(()=>pending.WaitAsync(TimeSpan.FromSeconds(5)));
                    Console.WriteLine($"Calendar root wait/cancellation drained: {provider}, tenant={tenantId.HasValue}");
                }
                finally
                {await cancellation.CancelAsync();await held.RollbackAsync();try{await pending.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){}}
            }
            async Task ReplayMessage(Guid id)
            {var operation=await db.QuerySingleAsync<Guid>("SELECT OperationId FROM fn_ai_schedule_completion_intent WHERE RunId=@Id",new{Id=id});var serializer=factory.Services.GetRequiredService<IIntegrationEventSerializer>();var message=new AiScheduleCompletionRequested(operation,tenantId);await using var scope=factory.Services.CreateAsyncScope();var handler=scope.ServiceProvider.GetServices<IIntegrationEventHandler>().Single(h=>h.EventType==AiScheduleCompletionRequested.EventType);await handler.HandleAsync(new(Guid.CreateVersion7(),handler.EventType,1,tenantId,null,DateTimeOffset.UtcNow),serializer.Serialize(message),default);}
            async Task<AiPersonalScheduleCompletionStatus> WaitResult(Guid id,string expected)
            {var timer=Stopwatch.StartNew();AiPersonalScheduleCompletionStatus? value=null;while(timer.Elapsed<TimeSpan.FromSeconds(45))
                {using var request=new HttpRequestMessage(HttpMethod.Get,StatusPath(id));request.Headers.Authorization=new("Bearer",owner);using var response=await client.SendAsync(request);
                 if(response.StatusCode!=HttpStatusCode.NotFound){var json=await response.Content.ReadAsStringAsync();Assert.AreEqual(HttpStatusCode.OK,response.StatusCode,json);
                    value=JsonSerializer.Deserialize<AiPersonalScheduleCompletionStatus>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;if(value.State==expected)return value;}
                 await Task.Delay(200);}Assert.Fail("未观察到准确接收方结果："+value?.State);return null!;}
        }
        Task<int> Count(string table)=>db.QuerySingleAsync<int>("SELECT COUNT(*) FROM "+table);
        async Task Tick()
        {await using var scope=factory.Services.CreateAsyncScope();var sp=scope.ServiceProvider;var context=sp.GetRequiredService<ICurrentTenantContextWriter>();context.SetHost();var coordinator=new AiAgentRunCoordinator(factory.Services.GetRequiredService<IServiceScopeFactory>(),sp.GetRequiredService<AiAgentRunStore>(),sp.GetRequiredService<AiAgentWorkerHeartbeatService>(),AgentFrameworkRuntime.CreateRunner(),sp.GetRequiredService<IClock>(),sp.GetRequiredService<IIdGenerator>(),Options.Create(new AiAgentRuntimeOptions{AcceptsNewRuns=true,RuntimeVersion="1",PollMilliseconds=60000}),NullLogger<AiAgentRunCoordinator>.Instance);await coordinator.ProcessPendingAsync();}
        static string StatusPath(Guid id)=>$"/api/v1/ai/agent/runs/{id}/schedule-completion";
        async Task<T> Send<T>(HttpMethod method,string path,string token,object? body=null,HttpStatusCode status=HttpStatusCode.OK)
        {using var request=new HttpRequestMessage(method,path);request.Headers.Authorization=new("Bearer",token);if(body is not null)request.Content=JsonContent.Create(body);using var response=await client.SendAsync(request);var json=await response.Content.ReadAsStringAsync();Assert.AreEqual(status,response.StatusCode,path+" "+json);return JsonSerializer.Deserialize<T>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;}
        async Task Exact(HttpMethod method,string path,string token,object body,HttpStatusCode status)
        {using var request=new HttpRequestMessage(method,path){Content=JsonContent.Create(body)};request.Headers.Authorization=new("Bearer",token);using var response=await client.SendAsync(request);Assert.AreEqual(status,response.StatusCode,await response.Content.ReadAsStringAsync());}
    }

    private sealed class Faults{internal bool LoseSenderReply,LoseReceiverReply;internal int ReceiveCalls;}
    private sealed class LostReceipt(IPersonalScheduleCompletion inner,Faults faults):IPersonalScheduleCompletion
    {public Task<bool> CanCompleteAsync(Guid id,int version,SessionBindingSnapshot original,CancellationToken token=default)=>inner.CanCompleteAsync(id,version,original,token);
     public Task<PersonalScheduleCompletionReceipt?> FindReceiptAsync(PersonalScheduleCompletionRequest request,CancellationToken token=default)=>inner.FindReceiptAsync(request,token);
     public async Task<PersonalScheduleCompletionReceipt> ReceiveAsync(PersonalScheduleCompletionRequest request,CancellationToken token=default){Interlocked.Increment(ref faults.ReceiveCalls);var result=await inner.ReceiveAsync(request,token);if(faults.LoseReceiverReply){faults.LoseReceiverReply=false;throw new IOException("fixture: receiver committed, reply lost");}return result;}}
    private sealed class MixedAuthorization(IAgentRunExecutionContext context,AiToolAuthorizationPort api,AiBackgroundToolAuthorizationPort worker):IToolAuthorizationPort
    {public ValueTask<ToolActor?> AuthorizeAsync(string permission,CancellationToken token)=>context.Binding is null?api.AuthorizeAsync(permission,token):worker.AuthorizeAsync(permission,token);}
    private sealed class MixedAudit(IAgentRunExecutionContext context,AiToolAuditPort api,AiBackgroundToolAuditPort worker,Faults faults):IToolAuditPort
    {public ValueTask BeginAsync(ToolInvocation invocation,string permission,string status,string? code,CancellationToken token)=>context.Binding is null?api.BeginAsync(invocation,permission,status,code,token):worker.BeginAsync(invocation,permission,status,code,token);
     public async ValueTask CompleteAsync(Guid operation,string status,string? code,int bytes,int duration,CancellationToken token){if(context.Binding is null)await api.CompleteAsync(operation,status,code,bytes,duration,token);else await worker.CompleteAsync(operation,status,code,bytes,duration,token);if(faults.LoseSenderReply&&status=="succeeded"){faults.LoseSenderReply=false;throw new IOException("fixture: sender committed, reply lost");}}}
    private sealed class NoModelFactory:IAiModelClientFactory
    {internal int Created;public string ProviderKey=>"ollama";public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding,CancellationToken token){Interlocked.Increment(ref Created);throw new AssertFailedException("静态图不得创建模型客户端。");}public ValueTask<IEmbeddingGenerator<string,Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding,CancellationToken token)=>throw new AssertFailedException("静态图不得创建向量客户端。");}
}
