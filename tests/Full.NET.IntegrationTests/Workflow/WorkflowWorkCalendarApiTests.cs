using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双库验证日历发布并发、旧版本不变、有界分页、精确权限与租户隔离。</summary>
[TestClass]
public sealed class WorkflowWorkCalendarApiTests
{
    [TestMethod]
    public async Task Work_calendar_with_sql_server()=>await VerifyAsync(DatabaseProvider.SqlServer,await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Work_calendar_with_mysql()=>await VerifyAsync(DatabaseProvider.MySql,await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    private static async Task VerifyAsync(DatabaseProvider provider,string connection)
    {
        using var factory=new FullNetApiFactory(provider,connection);await factory.InitializeAsync(useSchemaTemplate:false);
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(connection):new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        using var client=factory.CreateClientForHost("localhost");var identity=await factory.CreateHostIdentityAsync("calendar-"+Guid.NewGuid().ToString("N"),[
            WorkflowPermissions.CalendarsRead,WorkflowPermissions.CalendarsCreate,WorkflowPermissions.CalendarsUpdate,WorkflowPermissions.CalendarsPublish]);
        client.DefaultRequestHeaders.Authorization=new("Bearer",identity.AccessToken);
        foreach(var invalidPath in new[]{"/api/v1/workflow/work-calendars?afterId="+Guid.Empty.ToString("D"),"/api/v1/workflow/work-calendars/"+Guid.Empty.ToString("D"),"/api/v1/workflow/work-calendars/versions/"+Guid.Empty.ToString("D")})
        {using var invalid=await client.GetAsync(invalidPath);Assert.AreEqual(HttpStatusCode.BadRequest,invalid.StatusCode);}
        object Draft(string end="17:00")=>new{schemaVersion=1,timeZoneKey="UTC",weekdays=Enumerable.Range(1,5).Select(day=>new{dayOfWeek=day,ranges=new[]{new{start="09:00",end}}}).ToArray(),exceptions=Array.Empty<object>()};
        var key="calendar."+Guid.NewGuid().ToString("N");var request=new{calendarKey=key,displayName="Weekdays",draft=Draft()};
        var created=await PostAsync(client,"/api/v1/workflow/work-calendars",request,HttpStatusCode.Created);var id=created.GetProperty("id").GetGuid();
        Assert.AreEqual(1,created.GetProperty("revision").GetInt64());Assert.AreEqual(JsonValueKind.Null,created.GetProperty("latestVersionId").ValueKind);
        await PostAsync(client,"/api/v1/workflow/work-calendars",request,HttpStatusCode.Conflict);
        await PostAsync(client,"/api/v1/workflow/work-calendars",new{calendarKey="spoof."+Guid.NewGuid().ToString("N"),displayName="Spoof",draft=Draft(),tenantId=Guid.NewGuid()},HttpStatusCode.BadRequest);
        await PostAsync(client,"/api/v1/workflow/work-calendars",new{calendarKey="invalid."+Guid.NewGuid().ToString("N"),displayName="Invalid",draft=new{
            schemaVersion=1,timeZoneKey="UTC",weekdays=new[]{new{dayOfWeek=1,ranges=new[]{new{start="09:00",end="12:00"},new{start="11:00",end="13:00"}}}}}},HttpStatusCode.BadRequest);

        var publishes=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>client.PostAsJsonAsync($"/api/v1/workflow/work-calendars/{id:D}/publish",new{expectedRevision=1})));
        Guid first;
        try
        {
            CollectionAssert.AreEquivalent(new[]{HttpStatusCode.OK,HttpStatusCode.Conflict},publishes.Select(result=>result.StatusCode).ToArray());
            var body=await publishes.Single(result=>result.StatusCode==HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>();first=body.GetProperty("id").GetGuid();
            Assert.AreEqual(1,body.GetProperty("versionNumber").GetInt32());Assert.AreEqual(64,body.GetProperty("contentHash").GetString()!.Length);
            Assert.IsFalse(body.GetProperty("calendar").TryGetProperty("timeZoneRules",out _));
        }
        finally{foreach(var response in publishes)response.Dispose();}
        Assert.AreEqual(1,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_calendar_version WHERE CalendarId=@id",new{id}));
        var snapshot=await db.ExecuteScalarAsync<string>("SELECT CanonicalJson FROM fn_workflow_calendar_version WHERE Id=@first",new{first});Assert.IsTrue(snapshot!.Contains("timeZoneRules",StringComparison.Ordinal));
        async Task Put(long revision,string end,HttpStatusCode expected)
        {using var response=await client.PutAsJsonAsync($"/api/v1/workflow/work-calendars/{id:D}/draft",new{expectedRevision=revision,displayName="Updated",draft=Draft(end)});Assert.AreEqual(expected,response.StatusCode,await response.Content.ReadAsStringAsync());}
        await Put(1,"18:00",HttpStatusCode.Conflict);await Put(2,"18:00",HttpStatusCode.OK);
        var second=await PostAsync(client,$"/api/v1/workflow/work-calendars/{id:D}/publish",new{expectedRevision=3});Assert.AreEqual(2,second.GetProperty("versionNumber").GetInt32());
        var original=await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/work-calendars/versions/{first:D}");
        Assert.AreEqual("17:00",original.GetProperty("calendar").GetProperty("weekdays")[0].GetProperty("ranges")[0].GetProperty("end").GetString());
        Assert.AreEqual(snapshot,await db.ExecuteScalarAsync<string>("SELECT CanonicalJson FROM fn_workflow_calendar_version WHERE Id=@first",new{first}));
        var versions=await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/work-calendars/{id:D}/versions");Assert.AreEqual(2,versions.GetProperty("items").GetArrayLength());
        Assert.AreEqual(2,versions.GetProperty("items")[0].GetProperty("versionNumber").GetInt32());
        var older=await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/work-calendars/{id:D}/versions?beforeVersionNumber=2");Assert.AreEqual(first,older.GetProperty("items")[0].GetProperty("id").GetGuid());

        using var reader=factory.CreateClientForHost("localhost");var readerIdentity=await factory.CreateHostIdentityAsync("calendar-reader-"+Guid.NewGuid().ToString("N"),[WorkflowPermissions.CalendarsRead]);
        reader.DefaultRequestHeaders.Authorization=new("Bearer",readerIdentity.AccessToken);
        await PostAsync(reader,"/api/v1/workflow/work-calendars",request,HttpStatusCode.Forbidden);
        await PostAsync(reader,$"/api/v1/workflow/work-calendars/{id:D}/publish",new{expectedRevision=4},HttpStatusCode.Forbidden);
        using(var denied=await reader.PutAsJsonAsync($"/api/v1/workflow/work-calendars/{id:D}/draft",new{expectedRevision=4,displayName="Denied",draft=Draft()}))Assert.AreEqual(HttpStatusCode.Forbidden,denied.StatusCode);

        for(var index=0;index<21;index++)await PostAsync(client,"/api/v1/workflow/work-calendars",new{calendarKey="page."+Guid.NewGuid().ToString("N"),displayName="Page",draft=Draft()},HttpStatusCode.Created);
        var page=await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow/work-calendars");Assert.AreEqual(20,page.GetProperty("items").GetArrayLength());
        var cursor=page.GetProperty("nextAfterId").GetGuid();var next=await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/work-calendars?afterId={cursor:D}");Assert.AreEqual(2,next.GetProperty("items").GetArrayLength());
        var seen=page.GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("id").GetGuid()).Concat(next.GetProperty("items").EnumerateArray().Select(item=>item.GetProperty("id").GetGuid())).ToArray();Assert.AreEqual(22,seen.Distinct().Count());
        Assert.IsFalse(page.GetRawText().Contains("timeZoneRules",StringComparison.Ordinal));Assert.IsFalse(page.GetRawText().Contains("weekdays",StringComparison.Ordinal));

        using var tenantClient=factory.CreateClientForHost("localhost");tenantClient.DefaultRequestHeaders.Add("Origin","http://localhost");
        var login=await PostAsync(tenantClient,"/api/v1/auth/login",new LoginRequest("admin",FullNetApiFactory.TestPassword));tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",login.GetProperty("accessToken").GetString());
        var tenants=await tenantClient.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available");var tenant=tenants!.Single(item=>item.Identifier=="acme");
        tenantClient.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(tenantClient,login.GetProperty("accessToken").GetString()!,tenant.Id));
        using(var foreign=await tenantClient.GetAsync($"/api/v1/workflow/work-calendars/{id:D}"))Assert.AreEqual(HttpStatusCode.NotFound,foreign.StatusCode);
        using(var foreignVersion=await tenantClient.GetAsync($"/api/v1/workflow/work-calendars/versions/{first:D}"))Assert.AreEqual(HttpStatusCode.NotFound,foreignVersion.StatusCode);
        await PostAsync(tenantClient,$"/api/v1/workflow/work-calendars/{id:D}/publish",new{expectedRevision=4},HttpStatusCode.NotFound);
        var tenantCalendar=await PostAsync(tenantClient,"/api/v1/workflow/work-calendars",request,HttpStatusCode.Created);var tenantId=tenantCalendar.GetProperty("id").GetGuid();
        var tenantVersion=await PostAsync(tenantClient,$"/api/v1/workflow/work-calendars/{tenantId:D}/publish",new{expectedRevision=1});
        Assert.AreNotEqual(first,tenantVersion.GetProperty("id").GetGuid());
        using(var foreign=await client.GetAsync($"/api/v1/workflow/work-calendars/{tenantId:D}"))Assert.AreEqual(HttpStatusCode.NotFound,foreign.StatusCode);
        using(var foreignCursor=await client.GetAsync($"/api/v1/workflow/work-calendars?afterId={tenantId:D}"))Assert.AreEqual(HttpStatusCode.NotFound,foreignCursor.StatusCode);

        var count=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_calendar_version");
        await db.ExecuteAsync("DROP INDEX ix_fn_workflow_calendar_scope_created ON fn_workflow_calendar");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"ALTER TABLE dbo.fn_workflow_calendar DROP CONSTRAINT fk_fn_workflow_calendar_latest":"ALTER TABLE fn_workflow_calendar DROP FOREIGN KEY fk_fn_workflow_calendar_latest");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer?"DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%314_WorkflowWorkingCalendars.sql'":"DELETE FROM schemaversions WHERE ScriptName LIKE '%314_WorkflowWorkingCalendars.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection);await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection);
        Assert.AreEqual(count,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_calendar_version"));Assert.AreEqual(snapshot,await db.ExecuteScalarAsync<string>("SELECT CanonicalJson FROM fn_workflow_calendar_version WHERE Id=@first",new{first}));
    }
}
