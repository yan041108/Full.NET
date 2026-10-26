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
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证固定请求、独立尝试、未知结果、人工对账以及暂停取消时的结果屏障。</summary>
[TestClass]
public sealed class WorkflowConnectorApiTests
{
    [TestMethod]
    public async Task Connector_with_sql_server()=>await VerifyAsync(DatabaseProvider.SqlServer,await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Connector_with_mysql()=>await VerifyAsync(DatabaseProvider.MySql,await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider,string connection)
    {
        var clock=new TestClock { UtcNow=DateTimeOffset.UtcNow }; var transport=new Transport(); var secretAvailable=false;
        WorkflowFormSchema Schema(string key,string type)=>new(1,1,[new("main",[new(key,type,true,new Dictionary<string,JsonElement>())])]);
        WorkflowHttpConnectorRegistration Registration(bool idempotent)=>new(
            idempotent?"credit.check":"credit.once",1,new Uri("https://connector.example.org/check"),new HashSet<string>{"host"},
            Schema("subject","text"),Schema("score","integer"),idempotent,2,3,null);
        var registry=new WorkflowConnectorCatalog([Registration(true),Registration(false),Registration(false) with{ConnectorKey="credit.secret",BearerSecretReference="env:WORKFLOW_CONNECTOR_TEST"}]);
        using var factory=new FullNetApiFactory(provider,connection,configureTestServices:services=>{
            services.Replace(ServiceDescriptor.Singleton<IClock>(clock)); services.Replace(ServiceDescriptor.Singleton(registry));
            services.Replace(ServiceDescriptor.Singleton(new WorkflowConnectorHttpClient(_=>new HttpClient(transport,false),_=>secretAvailable?"test-secret":null)));
        });
        await factory.InitializeAsync(useSchemaTemplate:false); clock.UtcNow=DateTimeOffset.UtcNow;
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(connection):new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        if(provider==DatabaseProvider.SqlServer)
        {
            // datetime2(6)会向上舍入第七位；固定时钟须与正式存储精度相同，不能让立即到期活动落在未来100ns。
            var raw=DateTime.Parse("2026-10-26T00:00:00.1234569",System.Globalization.CultureInfo.InvariantCulture);
            var rounded=await db.ExecuteScalarAsync<DateTime>("SELECT CAST(CAST('2026-10-26T00:00:00.1234569' AS datetime2(7)) AS datetime2(6))");
            Assert.IsTrue(rounded>raw);
        }
        using var client=factory.CreateClientForHost("localhost");
        var identity=await factory.CreateHostIdentityAsync("connector-"+Guid.NewGuid().ToString("N"),[
            WorkflowPermissions.FormsCreate,WorkflowPermissions.FormsPublish,WorkflowPermissions.DefinitionsCreate,WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.DefinitionsRead,WorkflowPermissions.InstancesStart,WorkflowPermissions.InstancesRead,WorkflowPermissions.InstancesPause,
            WorkflowPermissions.InstancesResume,WorkflowPermissions.InstancesCancel,WorkflowPermissions.ConnectorCallsRead,WorkflowPermissions.ConnectorCallsReconcile]);
        client.DefaultRequestHeaders.Authorization=new("Bearer",identity.AccessToken);
        var directory=(await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow/definitions/connectors")).GetRawText();
        Assert.IsFalse(directory.Contains("connector.example.org",StringComparison.Ordinal)); Assert.IsTrue(directory.Contains("credit.check",StringComparison.Ordinal));
        var form=await PostAsync(client,"/api/v1/workflow/forms",new { formKey="connector."+Guid.NewGuid().ToString("N"),draft=new {
            schemaVersion=1,adapterVersion=1,sections=new[]{new{sectionKey="main",fields=new object[]{
                new{fieldKey="title",fieldTypeKey="text",required=true,constraints=new{}},new{fieldKey="count",fieldTypeKey="integer",required=false,constraints=new{}},
                new{fieldKey="private",fieldTypeKey="text",required=false,constraints=new{}} }}}}},HttpStatusCode.Created);
        var formVersion=(await PostAsync(client,$"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish",new{expectedRevision=1})).GetProperty("id").GetGuid();
        async Task<Guid> Publish(string key)=>await PublishNodesAsync(client,formVersion,[
            Node("start","start",new{nextNodeKeys=new[]{"call"}}),
            Node("call","integration.trigger",new{nextNodeKeys=new[]{"route"},fieldPolicies=new{title="readOnly",count="editable"},connectorPolicy=new{
                connectorKey=key,connectorVersion=1,inputFields=new{subject="title"},outputFields=new{count="score"}}}),
            Node("route","gateway.exclusive",new{nextNodeKeys=new[]{"yes","no"},branches=new[]{new{branchKey="yes",nextNodeKey="yes",condition=new{fieldKey="count",@operator="equals",value=7}}},defaultNextNodeKey="no"}),
            Node("yes","control.empty",new{nextNodeKeys=new[]{"end"}}),Node("no","control.empty",new{nextNodeKeys=new[]{"end"}}),Node("end","end",new{})]);
        var version=await Publish("credit.check"); var once=await Publish("credit.once"); var protectedVersion=await Publish("credit.secret");
        async Task<Guid> Start(Guid asset)=>(await PostAsync(client,"/api/v1/workflow/instances",new{definitionVersionId=asset,businessType="connector",
            businessId=Guid.NewGuid().ToString("N"),initialValues=new{title="subject",count=1,@private="do not send"},idempotencyKey=Guid.NewGuid().ToString("N")},HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task<string?> Status(Guid id)=>await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id",new{id});
        async Task<JsonElement> Call(Guid id)=>(await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{id:D}/connector-calls")).GetProperty("items")[0];
        async Task<long> Revision(Guid id)=>await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@id",new{id});
        async Task Operate(Guid id,string action)=>await PostAsync(client,$"/api/v1/workflow/instances/{id:D}/{action}",new{expectedRevision=await Revision(id),idempotencyKey=Guid.NewGuid().ToString("N")});

        var successful=await Start(version);
        Assert.AreEqual(clock.UtcNow.UtcDateTime,await db.ExecuteScalarAsync<DateTime>("SELECT DueAtUtc FROM fn_workflow_connector_call WHERE InstanceId=@successful",new{successful}));
        Assert.AreEqual(0,await WorkAsync(factory)); Assert.AreEqual(1,await ProcessAsync(factory));
        Assert.AreEqual("applied",(await Call(successful)).GetProperty("statusKey").GetString()); Assert.AreEqual(1,await WorkAsync(factory));
        Assert.AreEqual("completed",await Status(successful)); Assert.AreEqual(0,await ProcessAsync(factory));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_step WHERE InstanceId=@successful AND NodeKey='yes'",new{successful}));
        Assert.AreEqual("{\"subject\":\"subject\"}",transport.Bodies.Single()); Assert.AreEqual(1,transport.Keys.Distinct().Count());

        // 同一活动重试只增加尝试，稳定业务键与固定请求不随租约变化。
        transport.Next=_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var retry=await Start(version); Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual("retryable",(await Call(retry)).GetProperty("statusKey").GetString());
        Assert.AreEqual(0,await ProcessAsync(factory)); clock.UtcNow=clock.UtcNow.AddSeconds(6); transport.Next=_=>Task.FromResult(Success());
        Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual(1,await WorkAsync(factory)); Assert.AreEqual("completed",await Status(retry));
        Assert.AreEqual(transport.Keys[^1],transport.Keys[^2]); Assert.AreEqual(2,(await Call(retry)).GetProperty("attempts").GetArrayLength());

        // 远端可能执行的非幂等失败必须挂起；普通恢复和运维盲重试均不发送第二次HTTP。
        transport.Next=_=>throw new HttpRequestException("test transport interrupted"); var unknown=await Start(once);
        Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual("suspended",await Status(unknown)); var uncertain=await Call(unknown);
        Assert.AreEqual("outcome_unknown",uncertain.GetProperty("statusKey").GetString()); var sent=transport.Keys.Count;
        await PostAsync(client,$"/api/v1/workflow/instances/{unknown:D}/resume",new{expectedRevision=await Revision(unknown),idempotencyKey="blocked"},HttpStatusCode.Conflict);
        var reconcilePath=$"/api/v1/workflow/instances/{unknown:D}/connector-calls/{uncertain.GetProperty("id").GetGuid():D}/reconcile";
        await PostAsync(client,reconcilePath,new{expectedInstanceRevision=await Revision(unknown),expectedRevision=uncertain.GetProperty("revision").GetInt64(),actionKey="retry",reason="operator verified",evidenceReference="ticket:1",idempotencyKey="unsafe-retry"},HttpStatusCode.Conflict);
        Assert.AreEqual(0,await ProcessAsync(factory)); Assert.AreEqual(sent,transport.Keys.Count);
        await Operate(unknown,"cancel"); uncertain=await Call(unknown);
        var resolution=new{expectedInstanceRevision=await Revision(unknown),expectedRevision=uncertain.GetProperty("revision").GetInt64(),actionKey="confirm_no_effect",reason="owner confirmed no effect",evidenceReference="ticket:confirmed",idempotencyKey="confirmed"};
        var receipt=await PostAsync(client,reconcilePath,resolution); Assert.AreEqual(receipt.GetRawText(),(await PostAsync(client,reconcilePath,resolution)).GetRawText());
        Assert.IsFalse((await Call(unknown)).GetProperty("requiresReconciliation").GetBoolean()); Assert.AreEqual("cancelled",await Status(unknown));

        // 在实际HTTP等待中暂停，证明网络调用不持有实例锁；回执保留到恢复后再接纳。
        var paused=await Start(version); transport.Next=async _=>{await Operate(paused,"pause"); return Success();};
        Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual("suspended",await Status(paused));
        Assert.AreEqual("result_ready",(await Call(paused)).GetProperty("statusKey").GetString()); Assert.AreEqual(0,await WorkAsync(factory));
        await Operate(paused,"resume"); Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual(1,await WorkAsync(factory)); Assert.AreEqual("completed",await Status(paused));

        var cancelled=await Start(version); transport.Next=async _=>{await Operate(cancelled,"cancel"); return Success();};
        Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual("cancelled",await Status(cancelled)); var cancelledCall=await Call(cancelled);
        Assert.IsTrue(cancelledCall.GetProperty("requiresReconciliation").GetBoolean());
        Assert.AreNotEqual(JsonValueKind.Null,cancelledCall.GetProperty("completedAtUtc").ValueKind);
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_result WHERE InstanceId=@cancelled",new{cancelled}));
        Assert.AreEqual(0,await WorkAsync(factory)); Assert.AreEqual(0,await ProcessAsync(factory));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_connector_attempt WHERE CallId=@call AND OutcomeKey='succeeded'",new{call=cancelledCall.GetProperty("id").GetGuid()}));

        // 原HTTP尚未返回时另一Worker收回租约。迟到成功只写原尝试，须人工按原尝试对账接纳。
        var late=await Start(once); transport.Next=async _=>{clock.UtcNow=clock.UtcNow.AddSeconds(121); Assert.AreEqual(1,await ProcessAsync(factory)); return Success();};
        Assert.AreEqual(1,await ProcessAsync(factory)); var lateCall=await Call(late);
        Assert.AreEqual("suspended",await Status(late)); Assert.AreEqual("outcome_unknown",lateCall.GetProperty("statusKey").GetString());
        Assert.AreEqual("succeeded",lateCall.GetProperty("attempts")[0].GetProperty("outcomeKey").GetString());
        var latePath=$"/api/v1/workflow/instances/{late:D}/connector-calls/{lateCall.GetProperty("id").GetGuid():D}/reconcile";
        var accept=new{expectedInstanceRevision=await Revision(late),expectedRevision=lateCall.GetProperty("revision").GetInt64(),actionKey="accept_result",
            attemptId=lateCall.GetProperty("attempts")[0].GetProperty("id").GetGuid(),reason="verified original result",evidenceReference="ticket:late",idempotencyKey="accept-late"};
        await PostAsync(client,latePath,new{accept.expectedInstanceRevision,accept.expectedRevision,accept.actionKey,attemptId=Guid.NewGuid(),accept.reason,accept.evidenceReference,idempotencyKey="forged"},HttpStatusCode.Conflict);
        var accepted=await PostAsync(client,latePath,accept); Assert.AreEqual(accepted.GetRawText(),(await PostAsync(client,latePath,accept)).GetRawText());
        Assert.AreEqual("suspended",await Status(late)); await Operate(late,"resume"); sent=transport.Keys.Count;
        Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual(sent,transport.Keys.Count); Assert.AreEqual(1,await WorkAsync(factory)); Assert.AreEqual("completed",await Status(late));

        // 确认尚未发送的认证准备失败可由运维重试，仍使用原活动、固定请求和稳定业务键。
        transport.Next=_=>Task.FromResult(Success()); var credentialFailure=await Start(protectedVersion); sent=transport.Keys.Count;
        Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual(sent,transport.Keys.Count); var protectedCall=await Call(credentialFailure);
        Assert.AreEqual("rejected",protectedCall.GetProperty("statusKey").GetString());
        var protectedPath=$"/api/v1/workflow/instances/{credentialFailure:D}/connector-calls/{protectedCall.GetProperty("id").GetGuid():D}/reconcile";
        var safeRetry=new{expectedInstanceRevision=await Revision(credentialFailure),expectedRevision=protectedCall.GetProperty("revision").GetInt64(),actionKey="retry",reason="credential restored",evidenceReference="ticket:credentials",idempotencyKey="safe-retry"};
        await PostAsync(client,protectedPath,new{expectedInstanceRevision=safeRetry.expectedInstanceRevision+1,safeRetry.expectedRevision,safeRetry.actionKey,safeRetry.reason,safeRetry.evidenceReference,idempotencyKey="stale"},HttpStatusCode.Conflict);
        var safeReceipt=await PostAsync(client,protectedPath,safeRetry); Assert.AreEqual(safeReceipt.GetRawText(),(await PostAsync(client,protectedPath,safeRetry)).GetRawText());
        secretAvailable=true; await Operate(credentialFailure,"resume"); Assert.AreEqual(1,await ProcessAsync(factory)); Assert.AreEqual(1,await WorkAsync(factory));
        Assert.AreEqual("completed",await Status(credentialFailure)); Assert.AreEqual(sent+1,transport.Keys.Count);
        Assert.AreEqual(2,(await Call(credentialFailure)).GetProperty("attempts").GetArrayLength());

        // 其他路径关闭原步骤后，即使实例仍活动也不能再次分发远端副作用。
        var closedStep=await Start(version); sent=transport.Keys.Count;
        await db.ExecuteAsync("UPDATE fn_workflow_step SET StatusKey='cancelled' WHERE InstanceId=@closedStep AND NodeKey='call'",new{closedStep});
        Assert.AreEqual(0,await ProcessAsync(factory)); Assert.AreEqual(sent,transport.Keys.Count); await Operate(closedStep,"cancel");

        clock.UtcNow=DateTimeOffset.UtcNow;
        using var reader=factory.CreateClientForHost("localhost"); var readerIdentity=await factory.CreateHostIdentityAsync("connector-reader-"+Guid.NewGuid().ToString("N"),[WorkflowPermissions.InstancesRead]);
        reader.DefaultRequestHeaders.Authorization=new("Bearer",readerIdentity.AccessToken);
        using(var denied=await reader.GetAsync($"/api/v1/workflow/instances/{successful:D}/connector-calls"))Assert.AreEqual(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var missing=await client.GetAsync($"/api/v1/workflow/instances/{Guid.NewGuid():D}/connector-calls/{cancelledCall.GetProperty("id").GetGuid():D}"))Assert.AreEqual(HttpStatusCode.NotFound,missing.StatusCode);
        using var tenantClient=factory.CreateClientForHost("localhost"); tenantClient.DefaultRequestHeaders.Add("Origin","http://localhost");
        var login=await PostAsync(tenantClient,"/api/v1/auth/login",new LoginRequest("admin",FullNetApiFactory.TestPassword));
        tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",login.GetProperty("accessToken").GetString());
        var tenants=await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");
        tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient,login.GetProperty("accessToken").GetString()!,tenants!.Single(item=>item.Identifier=="acme").Id));
        using(var foreign=await tenantClient.GetAsync($"/api/v1/workflow/instances/{successful:D}/connector-calls"))Assert.AreEqual(HttpStatusCode.NotFound,foreign.StatusCode);
        Assert.AreEqual(0,(await tenantClient.GetFromJsonAsync<JsonElement>("/api/v1/workflow/definitions/connectors")).GetProperty("items").GetArrayLength());

        var total=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_connector_attempt");
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_connector_call_instance ON fn_workflow_connector_call");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%312_WorkflowConnectorCalls.sql'":"DELETE FROM schemaversions WHERE ScriptName LIKE '%312_WorkflowConnectorCalls.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection);
        Assert.AreEqual(total,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_connector_attempt"));
        Assert.AreEqual(3,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_connector_resolution"));
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>(provider==DatabaseProvider.SqlServer
            ?"SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_workflow_connector_call') AND name='ix_fn_workflow_connector_call_instance'"
            :"SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_connector_call' AND INDEX_NAME='ix_fn_workflow_connector_call_instance' AND SEQ_IN_INDEX=1"));
        if(provider==DatabaseProvider.MySql)Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() AND ROUTINE_NAME='fn_workflow_restore_connector_312'"));
    }
    private static object Node(string key,string type,object config)=>new{nodeKey=key,nodeTypeKey=type,nodeSchemaVersion=1,config};
    private static HttpResponseMessage Success()=>new(HttpStatusCode.OK){Content=new StringContent("{\"score\":7}",Encoding.UTF8,"application/json")};
    private static async Task<int> ProcessAsync(FullNetApiFactory factory){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WorkflowConnectorProcessor>().ProcessPendingAsync(default);}
    private static async Task<int> WorkAsync(FullNetApiFactory factory){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);}
    private sealed class TestClock:IClock
    {
        private DateTimeOffset value;
        public DateTimeOffset UtcNow { get=>value; set=>this.value=new(value.UtcTicks-value.UtcTicks%TimeSpan.TicksPerMicrosecond,TimeSpan.Zero); }
    }
    private sealed class Transport:HttpMessageHandler
    {
        public List<string> Keys{get;}=[]; public List<string> Bodies{get;}=[];
        public Func<HttpRequestMessage,Task<HttpResponseMessage>> Next{get;set;}=_=>Task.FromResult(Success());
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Keys.Add(request.Headers.GetValues("Idempotency-Key").Single());Bodies.Add(await request.Content!.ReadAsStringAsync(token));return await Next(request);}
    }
}
