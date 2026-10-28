using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>真实双方会话验证授权控制面，不把尚未接线的协作运行节点计为通过。</summary>
[TestClass]
public sealed class WorkflowCollaborationGrantApiTests
{
    [TestMethod]
    public async Task Collaboration_grants_with_sql_server()=>await VerifyAsync(DatabaseProvider.SqlServer,await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Collaboration_grants_with_mysql()=>await VerifyAsync(DatabaseProvider.MySql,await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider,string connection)
    {
        using var factory=new FullNetApiFactory(provider,connection); await factory.InitializeAsync(useSchemaTemplate:false);
        await using DbConnection db=provider==DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var sourceIdentity=await factory.CreateHostIdentityAsync("collab.source."+Guid.NewGuid().ToString("N"),
            [WorkflowPermissions.FormsCreate,WorkflowPermissions.FormsPublish,WorkflowPermissions.DefinitionsCreate,WorkflowPermissions.DefinitionsPublish,
             WorkflowPermissions.CollaborationGrantsRead,WorkflowPermissions.CollaborationGrantsPropose,WorkflowPermissions.CollaborationGrantsAccept,WorkflowPermissions.CollaborationGrantsRevoke]);
        using var source=factory.CreateClientForHost("localhost"); source.DefaultRequestHeaders.Authorization=new("Bearer",sourceIdentity.AccessToken);
        using var owner=factory.CreateClientForHost("localhost"); owner.DefaultRequestHeaders.Add("Origin","http://localhost");
        var login=await PostAsync(owner,"/api/v1/auth/login",new LoginRequest("admin",FullNetApiFactory.TestPassword)); var adminToken=login.GetProperty("accessToken").GetString()!;
        owner.DefaultRequestHeaders.Authorization=new("Bearer",adminToken);
        var tenants=await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"); var tenant=tenants!.Single(value=>value.Identifier=="acme");
        var foreignIdentifier="cg-"+Guid.NewGuid().ToString("N")[..10];
        var foreignTenant=await PostAsync(owner,"/api/v1/tenancy/tenants",new ProvisionTenantRequest(foreignIdentifier,"协作第三方隔离",foreignIdentifier+".localhost"),HttpStatusCode.Created);
        using var target=factory.CreateClientForHost("localhost"); target.DefaultRequestHeaders.Add("Origin","http://localhost");
        target.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner,adminToken,tenant.Id));
        var targetActor=(await target.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        // 进入租户会旋转原会话；第三方必须独立登录，不能复用已失效的Host令牌。
        using var foreign=factory.CreateClientForHost("localhost"); foreign.DefaultRequestHeaders.Add("Origin","http://localhost");
        var foreignLogin=await PostAsync(foreign,"/api/v1/auth/login",new LoginRequest("admin",FullNetApiFactory.TestPassword));
        foreign.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(foreign,foreignLogin.GetProperty("accessToken").GetString()!,foreignTenant.GetProperty("id").GetGuid()));
        var hostAsset=await AssetAsync(source); var tenantAsset=await AssetAsync(target);
        await CaseAsync(factory,db,source,target,foreign,hostAsset,tenantAsset,null,tenant.Id,sourceIdentity.UserId,targetActor);
        await CaseAsync(factory,db,target,source,foreign,tenantAsset,hostAsset,tenant.Id,null,targetActor,sourceIdentity.UserId);
        var denied=await factory.CreateHostIdentityAsync("collab.denied."+Guid.NewGuid().ToString("N"),[]);
        using var deniedClient=factory.CreateClientForHost("localhost"); deniedClient.DefaultRequestHeaders.Authorization=new("Bearer",denied.AccessToken);
        using(var response=await deniedClient.PostAsJsonAsync("/api/v1/workflow/collaboration-grants",new { })) Assert.AreEqual(HttpStatusCode.Forbidden,response.StatusCode);
        var count=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant");
        var actions=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant_action");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer ? "DROP INDEX ux_fn_workflow_collaboration_grant_proposal ON fn_workflow_collaboration_grant" : "ALTER TABLE fn_workflow_collaboration_grant DROP INDEX ux_fn_workflow_collaboration_grant_proposal");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_collaboration_grant DROP CONSTRAINT ck_fn_workflow_collaboration_grant_state" : "ALTER TABLE fn_workflow_collaboration_grant DROP CHECK ck_fn_workflow_collaboration_grant_state");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%330_WorkflowCollaborationGrants.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%330_WorkflowCollaborationGrants.sql'");
        await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection); await WorkflowHumanMigrationRecoveryTests.MigrateAsync(provider,connection);
        Assert.AreEqual(count,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant"));
        Assert.AreEqual(actions,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant_action"));
    }

    private static async Task CaseAsync(FullNetApiFactory factory,DbConnection db,HttpClient source,HttpClient target,HttpClient foreign,
        Asset sourceAsset,Asset targetAsset,Guid? sourceTenant,Guid? targetTenant,Guid sourceActor,Guid targetActor)
    {
        const string path="/api/v1/workflow/collaboration-grants";
        var offer=new { schemaVersion=1,sourceDefinitionId=sourceAsset.DefinitionId,sourceFormVersionId=sourceAsset.FormVersionId,
            targetWorkflowKey=targetAsset.Key,inputFieldKeys=new[]{"title"},outputFieldKeys=new[]{"title"},maximumTargetActivations=64,expiresAtUtc=DateTimeOffset.UtcNow.AddHours(2).AddTicks(7) };
        var proposal=new { targetTenantId=targetTenant,targetScopeKey=targetTenant is null ? "host" : "tenant",idempotencyKey="proposal."+Guid.NewGuid().ToString("N"),offer };
        var proposed=await PostAsync(source,path,proposal); var id=proposed.GetProperty("id").GetGuid();
        Assert.AreEqual("proposed",proposed.GetProperty("statusKey").GetString()); Assert.AreEqual(1L,proposed.GetProperty("revision").GetInt64());
        Assert.AreEqual(WorkflowCollaborationGrantPolicy.ScopeKey(sourceTenant),proposed.GetProperty("sourceTenantScopeKey").GetString());
        var acceptance=new { expectedRevision=1,idempotencyKey="accept",acceptance=new { schemaVersion=1,targetDefinitionVersionId=targetAsset.VersionId,starterUserId=targetActor,
            maximumActivities=32,allowedUserIds=new[]{targetActor},inputFields=new Dictionary<string,string>{{"title","title"}},outputFields=new Dictionary<string,string>{{"title","title"}} } };
        await PostAsync(source,$"{path}/{id:D}/accept",acceptance,HttpStatusCode.NotFound);
        using(var response=await foreign.GetAsync($"{path}/{id:D}")) Assert.AreEqual(HttpStatusCode.NotFound,response.StatusCode);
        await PostAsync(foreign,$"{path}/{id:D}/accept",acceptance,HttpStatusCode.NotFound);
        await PostAsync(foreign,$"{path}/{id:D}/revoke",new { expectedRevision=1,idempotencyKey="foreign",reason="第三方不得撤销" },HttpStatusCode.NotFound);
        var missingUser=Guid.CreateVersion7();
        var bad=acceptance with { acceptance=acceptance.acceptance with { allowedUserIds=new[]{missingUser},starterUserId=missingUser } };
        await PostAsync(target,$"{path}/{id:D}/accept",bad,HttpStatusCode.BadRequest);
        var wrongVersion=acceptance with { acceptance=acceptance.acceptance with { targetDefinitionVersionId=sourceAsset.VersionId } };
        await PostAsync(target,$"{path}/{id:D}/accept",wrongVersion,HttpStatusCode.BadRequest);
        var expanded=acceptance with { acceptance=acceptance.acceptance with { outputFields=new Dictionary<string,string>{{"title","secret"}} } };
        await PostAsync(target,$"{path}/{id:D}/accept",expanded,HttpStatusCode.BadRequest);
        var active=await PostAsync(target,$"{path}/{id:D}/accept",acceptance);
        Assert.AreEqual("active",active.GetProperty("statusKey").GetString()); Assert.AreEqual(2L,active.GetProperty("revision").GetInt64());
        Assert.AreEqual(64,active.GetProperty("policyHash").GetString()!.Length);
        var safe=active.GetProperty("targetSummary").GetRawText(); Assert.IsFalse(safe.Contains("nodes",StringComparison.Ordinal)); Assert.IsFalse(safe.Contains("assignee",StringComparison.Ordinal));
        await AssertAuthorityAsync(factory,id,sourceTenant,targetTenant,true);
        Assert.AreEqual(active.GetRawText(),(await PostAsync(target,$"{path}/{id:D}/accept",acceptance)).GetRawText());
        await PostAsync(source,path,proposal with { offer=offer with { maximumTargetActivations=63 } },HttpStatusCode.Conflict);
        var revoke=new { expectedRevision=2,idempotencyKey="revoke",reason="双方停止协作：保留原同意和回执" };
        var revoked=await PostAsync(source,$"{path}/{id:D}/revoke",revoke); Assert.AreEqual(3L,revoked.GetProperty("revision").GetInt64());
        await AssertAuthorityAsync(factory,id,sourceTenant,targetTenant,false);
        Assert.AreEqual(revoke.reason,await db.ExecuteScalarAsync<string>("SELECT RevocationReason FROM fn_workflow_collaboration_grant WHERE Id=@id",new { id }));
        Assert.AreEqual(revoked.GetRawText(),(await PostAsync(source,$"{path}/{id:D}/revoke",revoke)).GetRawText());
        Assert.AreEqual(active.GetRawText(),(await PostAsync(target,$"{path}/{id:D}/accept",acceptance)).GetRawText());
        Assert.AreEqual(proposed.GetRawText(),(await PostAsync(source,path,proposal)).GetRawText());
        await PostAsync(target,$"{path}/{id:D}/revoke",revoke with { idempotencyKey="stale-peer" },HttpStatusCode.Conflict);
        Assert.AreEqual(3,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant_action WHERE GrantId=@id",new { id }));
        // 两个真实HTTP请求竞争同一修订；不能同时接受与撤销，也不能留下无回执状态。
        var raceProposal=proposal with { idempotencyKey="race."+Guid.NewGuid().ToString("N") };
        var raceId=(await PostAsync(source,path,raceProposal)).GetProperty("id").GetGuid();
        var responses=await Task.WhenAll(target.PostAsJsonAsync($"{path}/{raceId:D}/accept",acceptance),source.PostAsJsonAsync($"{path}/{raceId:D}/revoke",new { expectedRevision=1,idempotencyKey="race-revoke",reason="并发撤销" }));
        try { Assert.AreEqual(1,responses.Count(response=>response.StatusCode==HttpStatusCode.OK)); Assert.AreEqual(1,responses.Count(response=>response.StatusCode==HttpStatusCode.Conflict)); }
        finally { foreach(var response in responses) response.Dispose(); }
        Assert.AreEqual(2,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant_action WHERE GrantId=@raceId",new { raceId }));
        var raceState=await target.GetFromJsonAsync<JsonElement>($"{path}/{raceId:D}");
        if(raceState.GetProperty("statusKey").GetString()=="active") await PostAsync(source,$"{path}/{raceId:D}/revoke",revoke with { idempotencyKey="race-cleanup" });
    }

    private static async Task AssertAuthorityAsync(FullNetApiFactory factory,Guid id,Guid? sourceTenant,Guid? targetTenant,bool expected)
    {
        await using var scope=factory.Services.CreateAsyncScope(); var writer=scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>(); writer.SetHost();
        try
        {
            var row=await scope.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find,
                WorkflowSqlParameters.Create(("Id",id),("TenantScopeKey",WorkflowCollaborationGrantPolicy.ScopeKey(sourceTenant))),default);
            Assert.IsNotNull(row);
            Assert.AreEqual(expected,WorkflowCollaborationGrantPolicy.TryAuthorize(row,WorkflowCollaborationGrantPolicy.ScopeKey(sourceTenant),false,2,DateTimeOffset.UtcNow,out _,out _));
            Assert.AreEqual(expected,WorkflowCollaborationGrantPolicy.TryAuthorize(row,WorkflowCollaborationGrantPolicy.ScopeKey(targetTenant),true,2,DateTimeOffset.UtcNow,out _,out _));
        }
        finally { writer.Clear(); }
    }
    private static async Task<Asset> AssetAsync(HttpClient client)
    {
        var form=await PostAsync(client,"/api/v1/workflow/forms",new { formKey="collab."+Guid.NewGuid().ToString("N"),draft=new { schemaVersion=1,adapterVersion=1,sections=new[]{new { sectionKey="main",fields=new[]{new { fieldKey="title",fieldTypeKey="text",required=false,constraints=new { } } } } } } },HttpStatusCode.Created);
        var formVersion=(await PostAsync(client,$"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish",new { expectedRevision=1 })).GetProperty("id").GetGuid();
        var key="collab."+Guid.NewGuid().ToString("N");
        var definition=await PostAsync(client,"/api/v1/workflow/definitions",new { definitionKey=key,draft=new { schemaVersion=1,workflowKindKey="automation",nodes=new[]{
            new { nodeKey="start",nodeTypeKey="start",nodeSchemaVersion=1,config=JsonSerializer.SerializeToElement(new { nextNodeKeys=new[]{"end"} }) },
            new { nodeKey="end",nodeTypeKey="end",nodeSchemaVersion=1,config=JsonSerializer.SerializeToElement(new { }) } } } },HttpStatusCode.Created);
        var id=definition.GetProperty("id").GetGuid(); var version=(await PostAsync(client,$"/api/v1/workflow/definitions/{id:D}/publish",new { expectedRevision=1,formVersionId=formVersion })).GetProperty("id").GetGuid();
        return new(id,version,formVersion,key);
    }
    private sealed record Asset(Guid DefinitionId,Guid VersionId,Guid FormVersionId,string Key);
    private static async Task<JsonElement> PostAsync(HttpClient client,string path,object request,HttpStatusCode expected=HttpStatusCode.OK)
    { using var response=await client.PostAsJsonAsync(path,request); var text=await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected,response.StatusCode,path+": "+text); return JsonSerializer.Deserialize<JsonElement>(text); }
}
