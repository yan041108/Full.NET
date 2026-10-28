using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证异步请求、独立字段推进、依赖屏障及受信租户后台执行。</summary>
[TestClass]
public sealed class WorkflowAsyncConnectorApiTests
{
    [TestMethod]
    public async Task Async_connector_with_sql_server()=>await VerifyAsync(DatabaseProvider.SqlServer,await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Async_connector_with_mysql()=>await VerifyAsync(DatabaseProvider.MySql,await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider,string connection)
    {
        var clock=new TestClock{UtcNow=DateTimeOffset.UtcNow}; var transport=new Transport();
        var allowed=new HashSet<string>{"host"};
        WorkflowFormSchema Schema(string key,string type)=>new(1,1,[new("main",[new(key,type,true,new Dictionary<string,JsonElement>())])]);
        using var factory=new FullNetApiFactory(provider,connection,configureTestServices:services=>{
            services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
            // 注册表在首次使用时冻结，租户标识由正式登录获取，不伪造可信上下文。
            services.Replace(ServiceDescriptor.Singleton(_=>new WorkflowConnectorCatalog([new WorkflowHttpConnectorRegistration(
                "async.check",1,new Uri("https://connector.example.org/check"),allowed,Schema("subject","text"),Schema("score","integer"),true,2,3,null)])));
            services.Replace(ServiceDescriptor.Singleton(new WorkflowConnectorHttpClient(_=>new HttpClient(transport,false),_=>null)));
        });
        await factory.InitializeAsync(useSchemaTemplate:false); clock.UtcNow=DateTimeOffset.UtcNow;
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(connection):new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        using var tenantClient=factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Add("Origin","http://localhost");
        var login=await PostAsync(tenantClient,"/api/v1/auth/login",new LoginRequest("admin",FullNetApiFactory.TestPassword));
        tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",login.GetProperty("accessToken").GetString());
        var tenants=await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant=tenants!.Single(item=>item.Identifier=="acme");
        allowed.Add("tenant:"+tenant.Id.ToString("N"));
        tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient,login.GetProperty("accessToken").GetString()!,tenant.Id));
        using var client=factory.CreateClientForHost("localhost");
        var identity=await factory.CreateHostIdentityAsync("async-"+Guid.NewGuid().ToString("N"),[
            WorkflowPermissions.FormsCreate,WorkflowPermissions.FormsPublish,WorkflowPermissions.DefinitionsCreate,WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.InstancesStart,WorkflowPermissions.InstancesRead,WorkflowPermissions.InstancesPause,WorkflowPermissions.InstancesResume,
            WorkflowPermissions.InstancesCancel,WorkflowPermissions.ConnectorCallsRead]);
        client.DefaultRequestHeaders.Authorization=new("Bearer",identity.AccessToken);

        async Task<Guid> Version(HttpClient owner,bool independent,bool mapped=true,bool chain=false)
        {
            var form=await PostAsync(owner,"/api/v1/workflow/forms",new{formKey="async."+Guid.NewGuid().ToString("N"),draft=new{
                schemaVersion=1,adapterVersion=1,sections=new[]{new{sectionKey="main",fields=new object[]{
                    new{fieldKey="title",fieldTypeKey="text",required=true,constraints=new{}},new{fieldKey="count",fieldTypeKey="integer",required=false,constraints=new{}}}}}}},HttpStatusCode.Created);
            var formVersion=(await PostAsync(owner,$"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish",new{expectedRevision=1})).GetProperty("id").GetGuid();
            var nodes=new List<object>{Node("start","start",new{nextNodeKeys=new[]{"call"}}),
                Node("call","integration.async_trigger",new{nextNodeKeys=new[]{chain?"second":independent?"modify":"end"},fieldPolicies=new{title="readOnly",count="editable"},
                    connectorPolicy=new{connectorKey="async.check",connectorVersion=1,inputFields=new{subject="title"},outputFields=mapped?new Dictionary<string,string>{{"count","score"}}:new Dictionary<string,string>()}})};
            if(chain) nodes.Add(Node("second","integration.async_trigger",new{nextNodeKeys=new[]{"end"},fieldPolicies=new{title="readOnly",count="editable"},
                connectorPolicy=new{connectorKey="async.check",connectorVersion=1,inputFields=new{subject="title"},outputFields=new Dictionary<string,string>{{"count","score"}}}},["call"]));
            if(independent)nodes.AddRange([
                Node("modify","data.modify",new{nextNodeKeys=new[]{"consumer"},fieldPolicies=new{title="editable"},mutationPolicy=new{
                    assignments=new[]{new{fieldKey="title",value="independent"}}}}),
                Node("consumer","gateway.exclusive",new{nextNodeKeys=new[]{"yes","no"},branches=new[]{new{branchKey="yes",nextNodeKey="yes",condition=new{fieldKey="count",@operator="equals",value=7}}},defaultNextNodeKey="no"},["call"]),
                Node("yes","control.empty",new{nextNodeKeys=new[]{"end"}}),Node("no","control.empty",new{nextNodeKeys=new[]{"end"}})]);
            nodes.Add(Node("end","end",new{})); return await PublishNodesAsync(owner,formVersion,nodes.ToArray());
        }
        async Task<Guid> Start(HttpClient owner,Guid version)=>(await PostAsync(owner,"/api/v1/workflow/instances",new{
            definitionVersionId=version,businessType="async",businessId=Guid.NewGuid().ToString("N"),initialValues=new{title="original",count=1},idempotencyKey=Guid.NewGuid().ToString("N")},HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task<string?> Status(Guid id)=>await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id",new{id});
        async Task Operate(Guid id,string action)=>await PostAsync(client,$"/api/v1/workflow/instances/{id:D}/{action}",new{
            expectedRevision=await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@id",new{id}),idempotencyKey=Guid.NewGuid().ToString("N")});
        async Task DrainWork(){for(var index=0;index<8;index++)if(await Work(factory)==0)return;Assert.Fail("续办未在有界次数内收敛");}

        var version=await Version(client,true); var id=await Start(client,version); await DrainWork();
        Assert.AreEqual("active",await Status(id));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_async_barrier WHERE InstanceId=@id AND NodeKey='consumer' AND StatusKey='pending'",new{id}));
        Assert.AreEqual(0,await Barrier(factory)); Assert.AreEqual(0,transport.Bodies.Count);
        Assert.AreEqual(1,await Connector(factory));
        Assert.AreEqual("result_ready",await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_connector_call WHERE InstanceId=@id",new{id}));
        clock.UtcNow=clock.UtcNow.AddSeconds(2); var racing=await Task.WhenAll(Barrier(factory),Barrier(factory)); Assert.AreEqual(1,racing.Sum()); await DrainWork();
        Assert.AreEqual("completed",await Status(id));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@id AND NodeKey='yes'",new{id}));
        var values=await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id",new{id});
        using(var body=JsonDocument.Parse(values!)){Assert.AreEqual("independent",body.RootElement.GetProperty("title").GetString());Assert.AreEqual(7,body.RootElement.GetProperty("count").GetInt32());}
        Assert.AreEqual("{\"subject\":\"original\"}",transport.Bodies.Single()); Assert.AreEqual(0,await Barrier(factory));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@id AND ResumeBarrierId IS NOT NULL",new{id}));

        var terminalVersion=await Version(client,false); var paused=await Start(client,terminalVersion); await DrainWork();
        transport.OnSend=()=>Operate(paused,"pause"); Assert.AreEqual(1,await Connector(factory)); transport.OnSend=null;
        Assert.AreEqual("suspended",await Status(paused)); Assert.AreEqual(0,await Barrier(factory));
        Assert.AreEqual("result_ready",await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_connector_call WHERE InstanceId=@paused",new{paused}));
        await Operate(paused,"resume"); Assert.AreEqual(1,await Barrier(factory)); await DrainWork();
        Assert.AreEqual("completed",await Status(paused));
        var cancelled=await Start(client,terminalVersion); await DrainWork(); transport.OnSend=()=>Operate(cancelled,"cancel"); Assert.AreEqual(1,await Connector(factory)); transport.OnSend=null;
        Assert.AreEqual("cancelled",await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_async_barrier WHERE InstanceId=@cancelled",new{cancelled}));
        Assert.AreEqual(0,await Connector(factory)); Assert.AreEqual(0,await Barrier(factory));

        // 模拟结果已事务接纳、屏障尚未释放时进程退出；恢复时损坏的当前表单必须明确挂起。
        var damaged=await Start(client,terminalVersion); await DrainWork(); Assert.AreEqual(1,await Connector(factory));
        var barrierId=await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_async_barrier WHERE InstanceId=@damaged",new{damaged});
        var callId=await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_connector_call WHERE InstanceId=@damaged",new{damaged});
        await db.ExecuteAsync("UPDATE fn_workflow_async_barrier SET StatusKey='applying',LeaseGeneration=LeaseGeneration+1,Revision=Revision+1,LeaseOwnerKey='crash-test',LeaseExpiresAtUtc=@expires WHERE Id=@barrierId",new{barrierId,expires=clock.UtcNow.AddSeconds(300).UtcDateTime});
        await using(var serviceScope=factory.Services.CreateAsyncScope())
        {
            var queries=serviceScope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var barrier=await queries.QuerySingleOrDefaultAsync<WorkflowAsyncBarrierRecord>(WorkflowAsyncBarrierSql.Find,
                WorkflowSqlParameters.Create(("Id",barrierId),("InstanceId",damaged),("TenantScopeKey","host")),default);
            var call=await queries.QuerySingleOrDefaultAsync<WorkflowConnectorCallRecord>(WorkflowConnectorSql.Find,
                WorkflowSqlParameters.Create(("Id",callId),("TenantScopeKey","host")),default);
            Assert.IsTrue(await serviceScope.ServiceProvider.GetRequiredService<WorkflowConnectorProcessor>().ApplyForBarrierAsync(call!,barrier!,default));
        }
        await db.ExecuteAsync("UPDATE fn_workflow_form_submission SET SubmissionJson=@body,Revision=Revision+1 WHERE InstanceId=@damaged",new{damaged,body="{\"title\":123,\"count\":7}"});
        clock.UtcNow=clock.UtcNow.AddSeconds(301); Assert.AreEqual(1,await Barrier(factory)); Assert.AreEqual("suspended",await Status(damaged));
        Assert.AreEqual("workflow.activity.async_input_invalid",await db.ExecuteScalarAsync<string>("SELECT ErrorKey FROM fn_workflow_async_barrier WHERE Id=@barrierId",new{barrierId}));
        Assert.AreEqual(0,await Barrier(factory)); await Operate(damaged,"cancel");

        var noOutputVersion=await Version(client,false,false); var noOutput=await Start(client,noOutputVersion); await DrainWork();
        Assert.AreEqual("active",await Status(noOutput)); Assert.AreEqual(1,await Connector(factory)); Assert.AreEqual("active",await Status(noOutput));
        Assert.AreEqual(1,await Barrier(factory)); await DrainWork(); Assert.AreEqual("completed",await Status(noOutput));
        using(var unchanged=JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@noOutput",new{noOutput}))!))Assert.AreEqual(1,unchanged.RootElement.GetProperty("count").GetInt32());

        clock.UtcNow=DateTimeOffset.UtcNow;
        var tenantVersion=await Version(tenantClient,false); var tenantId=await Start(tenantClient,tenantVersion); await DrainWork();
        using(var foreign=await client.GetAsync($"/api/v1/workflow/instances/{tenantId:D}/connector-calls"))Assert.AreEqual(HttpStatusCode.NotFound,foreign.StatusCode);
        Assert.AreEqual(1,await Connector(factory)); Assert.AreEqual(1,await Barrier(factory)); await DrainWork();
        Assert.AreEqual("completed",await Status(tenantId));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_connector_call WHERE InstanceId=@tenantId AND TenantScopeKey=@scope AND StatusKey='applied'",new{tenantId,scope="tenant:"+tenant.Id.ToString("N")}));

        // 后一个连接器本身消费前一个异步结果，分发阶段也必须按真实来源代次复核原调用。
        foreach(var owner in new[]{client,tenantClient})
        {
            var chained=await Start(owner,await Version(owner,false,chain:true)); var requests=transport.Bodies.Count;
            for(var attempt=0;attempt<32 && await Status(chained)!="completed";attempt++)
            {await DrainWork();await Connector(factory);await Barrier(factory);await DrainWork();clock.UtcNow=clock.UtcNow.AddSeconds(2);}
            Assert.AreEqual("completed",await Status(chained)); Assert.AreEqual(requests+2,transport.Bodies.Count);
            Assert.AreEqual(2,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_connector_call WHERE InstanceId=@chained AND StatusKey='applied'",new{chained}));
            Assert.AreEqual(2,await db.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT Id) FROM fn_workflow_connector_call WHERE InstanceId=@chained",new{chained}));
            Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_async_barrier WHERE InstanceId=@chained AND NodeKey='second' AND StatusKey='released'",new{chained}));
            await WorkflowCausalityAssertions.CompleteRootAsync(db,chained);
        }

        foreach(var owner in new[]{client,tenantClient})
        foreach(var missing in new[]{"SourceStepId","CauseKindKey"})
        {
            var missingSource=await Start(owner,await Version(owner,false));
            Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@missingSource AND StatusKey='pending' AND SourceStepId IS NOT NULL",new { missingSource }));
            // 模拟旧记录或损坏来源；普通恢复不能把未知来源猜成根步骤。
            await db.ExecuteAsync($"UPDATE fn_workflow_activity_work SET {missing}=NULL WHERE InstanceId=@missingSource AND StatusKey='pending'",new { missingSource });
            await DrainWork();
            Assert.AreEqual("suspended",await Status(missingSource));
            Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work WHERE InstanceId=@missingSource AND StatusKey='faulted'",new { missingSource }));
            Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_execution_log WHERE InstanceId=@missingSource AND TransitionKey='activity.causality.invalid_source' AND Summary='workflow.causality.source_missing'",new { missingSource }));
        }

        var retained=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_async_barrier");
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_async_barrier_due ON fn_workflow_async_barrier");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%313_WorkflowAsyncBarriers.sql'":"DELETE FROM schemaversions WHERE ScriptName LIKE '%313_WorkflowAsyncBarriers.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection);
        Assert.AreEqual(retained,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_async_barrier"));
    }
    private static object Node(string key,string type,object config,string[]? awaited=null)=>new{nodeKey=key,nodeTypeKey=type,nodeSchemaVersion=1,config,awaitActivityNodeKeys=awaited};
    private static async Task<int> Connector(FullNetApiFactory factory){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WorkflowConnectorProcessor>().ProcessPendingAsync(default);}
    private static async Task<int> Barrier(FullNetApiFactory factory){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WorkflowAsyncBarrierProcessor>().ProcessPendingAsync(default);}
    private static async Task<int> Work(FullNetApiFactory factory){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);}
    private sealed class TestClock:IClock
    {private DateTimeOffset value;public DateTimeOffset UtcNow{get=>value;set=>this.value=new(value.UtcTicks-value.UtcTicks%TimeSpan.TicksPerMicrosecond,TimeSpan.Zero);}}
    private sealed class Transport:HttpMessageHandler
    {
        public List<string> Bodies{get;}=[]; public Func<Task>? OnSend{get;set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Bodies.Add(await request.Content!.ReadAsStringAsync(token));if(OnSend is not null)await OnSend();return new(HttpStatusCode.OK){Content=new StringContent("{\"score\":7}",Encoding.UTF8,"application/json")};}
    }
}
