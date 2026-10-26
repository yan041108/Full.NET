using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证工作日历绑定、暂停剩余工作量、原版本恢复及取消/租户边界。</summary>
[TestClass]
public sealed class WorkflowWorkingDelayApiTests
{
    [TestMethod]
    public async Task Working_delay_with_sql_server()=>await VerifyAsync(DatabaseProvider.SqlServer,await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Working_delay_with_mysql()=>await VerifyAsync(DatabaseProvider.MySql,await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider,string connection)
    {
        var clock=new TestClock{UtcNow=DateTimeOffset.UtcNow};using var factory=new FullNetApiFactory(provider,connection,configureTestServices:services=>{
            // 仅推进流程的业务时间，真实身份与会话继续使用宿主时钟和正常验证。
            services.Replace(ServiceDescriptor.Scoped<WorkflowInstanceManagementService>(scope=>ActivatorUtilities.CreateInstance<WorkflowInstanceManagementService>(scope,clock)));
            services.Replace(ServiceDescriptor.Scoped<WorkflowDelayProcessor>(scope=>ActivatorUtilities.CreateInstance<WorkflowDelayProcessor>(scope,clock)));
            services.Replace(ServiceDescriptor.Scoped<WorkflowActivityWorkProcessor>(scope=>ActivatorUtilities.CreateInstance<WorkflowActivityWorkProcessor>(scope,clock)));
        });
        await factory.InitializeAsync(useSchemaTemplate:false);clock.UtcNow=DateTimeOffset.UtcNow;
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(connection):new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        using var client=factory.CreateClientForHost("localhost");var identity=await factory.CreateHostIdentityAsync("working-delay-"+Guid.NewGuid().ToString("N"),[
            WorkflowPermissions.CalendarsRead,WorkflowPermissions.CalendarsCreate,WorkflowPermissions.CalendarsUpdate,WorkflowPermissions.CalendarsPublish,
            WorkflowPermissions.FormsCreate,WorkflowPermissions.FormsPublish,WorkflowPermissions.DefinitionsCreate,WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.InstancesStart,WorkflowPermissions.InstancesRead,WorkflowPermissions.InstancesPause,WorkflowPermissions.InstancesResume,WorkflowPermissions.InstancesCancel]);
        client.DefaultRequestHeaders.Authorization=new("Bearer",identity.AccessToken);
        object CalendarDraft(bool allDays=false)=>new{schemaVersion=1,timeZoneKey="UTC",weekdays=Enumerable.Range(allDays?0:1,allDays?7:5).Select(day=>new{dayOfWeek=day,ranges=new[]{new{start=allDays?"00:00":"09:00",end=allDays?"24:00":"17:00"}}}).ToArray(),exceptions=Array.Empty<object>()};
        async Task<(Guid Id,JsonElement Version)> Calendar(HttpClient owner)
        {
            var created=await PostAsync(owner,"/api/v1/workflow/work-calendars",new{calendarKey="working."+Guid.NewGuid().ToString("N"),displayName="Weekdays",draft=CalendarDraft()},HttpStatusCode.Created);
            var id=created.GetProperty("id").GetGuid();return(id,await PostAsync(owner,$"/api/v1/workflow/work-calendars/{id:D}/publish",new{expectedRevision=1}));
        }
        async Task<Guid> Form(HttpClient owner)
        {
            var created=await PostAsync(owner,"/api/v1/workflow/forms",new{formKey="working."+Guid.NewGuid().ToString("N"),draft=new{schemaVersion=1,adapterVersion=1,
                sections=new[]{new{sectionKey="main",fields=new[]{new{fieldKey="flag",fieldTypeKey="switch",required=false,constraints=new{}}}}}}},HttpStatusCode.Created);
            return(await PostAsync(owner,$"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish",new{expectedRevision=1})).GetProperty("id").GetGuid();
        }
        object[] Nodes(Guid calendarVersion)=>[Node("start","start",new{nextNodeKeys=new[]{"delay"}}),Node("delay","timer.delay",new{nextNodeKeys=new[]{"end"},delayPolicy=new{
            modeKey="working",timeZoneKey="UTC",duration=2,unitKey="hour",calendarVersionId=calendarVersion,boundCalendarHash="untrusted",boundCalendar=new{invalid=true}}}),Node("end","end",new{})];
        async Task<Guid> Start(HttpClient owner,Guid version)=>(await PostAsync(owner,"/api/v1/workflow/instances",new{definitionVersionId=version,businessType="working.delay",businessId=Guid.NewGuid().ToString("N"),initialValues=new{flag=true},idempotencyKey=Guid.NewGuid().ToString("N")},HttpStatusCode.Created)).GetProperty("id").GetGuid();
        async Task<long> Revision(Guid id)=>await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@id",new{id});
        async Task<string?> Status(Guid id)=>await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id",new{id});
        async Task Control(Guid id,string action,HttpStatusCode expected=HttpStatusCode.OK)=>await PostAsync(client,$"/api/v1/workflow/instances/{id:D}/{action}",new{expectedRevision=await Revision(id),idempotencyKey=Guid.NewGuid().ToString("N")},expected);
        async Task<DateTimeOffset> Due(Guid id)=>new(DateTime.SpecifyKind(await db.ExecuteScalarAsync<DateTime>("SELECT DueAtUtc FROM fn_workflow_delay WHERE InstanceId=@id",new{id}),DateTimeKind.Utc));

        var calendar=await Calendar(client);var calendarVersion=calendar.Version.GetProperty("id").GetGuid();var originalHash=calendar.Version.GetProperty("contentHash").GetString();
        var form=await Form(client);var version=await PublishNodesAsync(client,form,Nodes(calendarVersion));
        clock.UtcNow=At("2026-10-30T16:00:00Z");var id=await Start(client,version);Assert.AreEqual(At("2026-11-02T10:00:00Z"),await Due(id));
        Assert.AreEqual(calendarVersion,await db.ExecuteScalarAsync<Guid>("SELECT CalendarVersionId FROM fn_workflow_delay WHERE InstanceId=@id",new{id}));
        Assert.AreEqual(originalHash,await db.ExecuteScalarAsync<string>("SELECT CalendarHash FROM fn_workflow_delay WHERE InstanceId=@id",new{id}));
        clock.UtcNow=At("2026-10-30T16:30:00Z");var pause=new{expectedRevision=await Revision(id),idempotencyKey="freeze-work"};
        var paused=await PostAsync(client,$"/api/v1/workflow/instances/{id:D}/pause",pause);Assert.AreEqual(paused.GetRawText(),(await PostAsync(client,$"/api/v1/workflow/instances/{id:D}/pause",pause)).GetRawText());
        Assert.AreEqual(90*60_000L,await db.ExecuteScalarAsync<long>("SELECT RemainingMilliseconds FROM fn_workflow_delay WHERE InstanceId=@id",new{id}));
        using(var update=await client.PutAsJsonAsync($"/api/v1/workflow/work-calendars/{calendar.Id:D}/draft",new{expectedRevision=2,displayName="Always open",draft=CalendarDraft(true)}))Assert.AreEqual(HttpStatusCode.OK,update.StatusCode);
        await PostAsync(client,$"/api/v1/workflow/work-calendars/{calendar.Id:D}/publish",new{expectedRevision=3});
        clock.UtcNow=At("2026-10-31T09:00:00Z");Assert.AreEqual(0,await Delay(factory));await Control(id,"resume");
        Assert.AreEqual(At("2026-11-02T10:30:00Z"),await Due(id));
        Assert.AreEqual(At("2026-11-02T10:30:00Z").UtcDateTime,await db.ExecuteScalarAsync<DateTime>("SELECT DueAtUtc FROM fn_workflow_step WHERE InstanceId=@id AND NodeKey='delay'",new{id}));
        clock.UtcNow=At("2026-11-02T10:29:59Z");Assert.AreEqual(0,await Delay(factory));clock.UtcNow=At("2026-11-02T10:30:00Z");
        Assert.AreEqual(1,(await Task.WhenAll(Delay(factory),Delay(factory))).Sum());Assert.AreEqual(1,await Work(factory));Assert.AreEqual("completed",await Status(id));

        clock.UtcNow=At("2026-10-30T16:00:00Z");var corrupt=await Start(client,version);clock.UtcNow=At("2026-10-30T16:30:00Z");await Control(corrupt,"pause");
        var frozenRevision=await Revision(corrupt);await db.ExecuteAsync("UPDATE fn_workflow_delay SET CalendarHash=@hash WHERE InstanceId=@corrupt",new{corrupt,hash=new string('f',64)});
        clock.UtcNow=At("2026-10-31T09:00:00Z");await Control(corrupt,"resume",HttpStatusCode.Conflict);Assert.AreEqual("suspended",await Status(corrupt));Assert.AreEqual(frozenRevision,await Revision(corrupt));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@corrupt AND ActionKey='resume'",new{corrupt}));
        await db.ExecuteAsync("UPDATE fn_workflow_delay SET CalendarHash=@originalHash WHERE InstanceId=@corrupt",new{corrupt,originalHash});await Control(corrupt,"cancel");
        var cancelled=await Start(client,version);await Control(cancelled,"cancel");clock.UtcNow=At("2026-11-03T10:00:00Z");Assert.AreEqual(0,await Delay(factory));Assert.AreEqual(0,await Work(factory));

        clock.UtcNow=DateTimeOffset.UtcNow;using var tenantClient=factory.CreateClientForHost("localhost");tenantClient.DefaultRequestHeaders.Add("Origin","http://localhost");
        var login=await PostAsync(tenantClient,"/api/v1/auth/login",new LoginRequest("admin",FullNetApiFactory.TestPassword));tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",login.GetProperty("accessToken").GetString());
        var tenants=await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");var tenant=tenants!.Single(item=>item.Identifier=="acme");tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient,login.GetProperty("accessToken").GetString()!,tenant.Id));
        var tenantCalendar=await Calendar(tenantClient);var tenantForm=await Form(tenantClient);
        async Task RejectForeign(HttpClient owner,Guid ownForm,Guid foreignVersion)
        {
            var draft=await PostAsync(owner,"/api/v1/workflow/definitions",new{definitionKey="foreign.calendar."+Guid.NewGuid().ToString("N"),draft=new{schemaVersion=1,workflowKindKey="automation",nodes=Nodes(foreignVersion)}},HttpStatusCode.Created);
            await PostAsync(owner,$"/api/v1/workflow/definitions/{draft.GetProperty("id").GetGuid():D}/publish",new{expectedRevision=1,formVersionId=ownForm},HttpStatusCode.BadRequest);
        }
        await RejectForeign(client,form,tenantCalendar.Version.GetProperty("id").GetGuid());await RejectForeign(tenantClient,tenantForm,calendarVersion);
        var tenantVersion=await PublishNodesAsync(tenantClient,tenantForm,Nodes(tenantCalendar.Version.GetProperty("id").GetGuid()));var tenantInstance=await Start(tenantClient,tenantVersion);
        using(var foreign=await client.GetAsync($"/api/v1/workflow/instances/{tenantInstance:D}"))Assert.AreEqual(HttpStatusCode.NotFound,foreign.StatusCode);
        clock.UtcNow=(await Due(tenantInstance)).AddSeconds(1);Assert.AreEqual(1,await Delay(factory));Assert.AreEqual(1,await Work(factory));Assert.AreEqual("completed",await Status(tenantInstance));
    }
    private static object Node(string key,string type,object config)=>new{nodeKey=key,nodeTypeKey=type,nodeSchemaVersion=1,config};
    private static async Task<int> Delay(FullNetApiFactory factory){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WorkflowDelayProcessor>().ProcessPendingAsync(default);}
    private static async Task<int> Work(FullNetApiFactory factory){await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);}
    private static DateTimeOffset At(string value)=>DateTimeOffset.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
    private sealed class TestClock:IClock{private DateTimeOffset value;public DateTimeOffset UtcNow{get=>value;set=>this.value=new(value.UtcTicks-value.UtcTicks%TimeSpan.TicksPerMicrosecond,TimeSpan.Zero);}}
}
