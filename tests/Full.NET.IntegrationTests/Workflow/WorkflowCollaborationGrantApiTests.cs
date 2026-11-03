using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using DbUp;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
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
        owner.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner,adminToken,tenant.Id));
        // 双方使用独立用户；同一客户端再次登录admin会撤销目标会话，掩盖真正的隔离断言。
        var targetName="collab.target."+Guid.NewGuid().ToString("N");
        var targetIdentity=await factory.CreateHostIdentityAsync(targetName,
            [WorkflowPermissions.FormsCreate,WorkflowPermissions.FormsPublish,WorkflowPermissions.DefinitionsCreate,WorkflowPermissions.DefinitionsPublish,
             WorkflowPermissions.CollaborationGrantsRead,WorkflowPermissions.CollaborationGrantsPropose,WorkflowPermissions.CollaborationGrantsAccept,WorkflowPermissions.CollaborationGrantsRevoke,
             "tenancy.tenants.switch"],password:FullNetApiFactory.TestPassword);
        var email=targetName+"@example.com";
        await factory.EnsureHostUserProfileEmailAsync(targetIdentity.UserId,email,targetName);
        using var target=factory.CreateClientForHost("localhost"); target.DefaultRequestHeaders.Add("Origin","http://localhost");
        var targetHostToken=await IntegrationTestAuthHelper.LoginAsHostUserAsync(target,targetName,FullNetApiFactory.TestPassword);
        target.DefaultRequestHeaders.Authorization=new("Bearer",targetHostToken);
        var invitation=await PostAsync(owner,"/api/v1/identity/tenant-members/invitations",new CreateTenantInvitationRequest(email,TenantMemberRoles.Member));
        await PostAsync(target,$"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept",new { });
        target.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(target,targetHostToken,tenant.Id));
        var targetActor=targetIdentity.UserId;
        using var foreign=factory.CreateClientForHost("localhost"); foreign.DefaultRequestHeaders.Add("Origin","http://localhost");
        foreign.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner,owner.DefaultRequestHeaders.Authorization!.Parameter!,foreignTenant.GetProperty("id").GetGuid()));
        var hostAsset=await AssetAsync(source); var tenantAsset=await AssetAsync(target);
        await CaseAsync(factory,db,source,target,foreign,hostAsset,tenantAsset,null,tenant.Id,sourceIdentity.UserId,targetActor);
        await CaseAsync(factory,db,target,source,foreign,tenantAsset,hostAsset,tenant.Id,null,targetActor,sourceIdentity.UserId);
        var foreignId=foreignTenant.GetProperty("id").GetGuid();
        // 超级管理员能切换新租户不代表已成为其活动成员；执行身份必须由真实邀请建立。
        var foreignName="collab.foreign."+Guid.NewGuid().ToString("N");
        var foreignIdentity=await factory.CreateHostIdentityAsync(foreignName,
            [WorkflowPermissions.FormsCreate,WorkflowPermissions.FormsPublish,WorkflowPermissions.DefinitionsCreate,WorkflowPermissions.DefinitionsPublish,
             WorkflowPermissions.CollaborationGrantsRead,WorkflowPermissions.CollaborationGrantsPropose,WorkflowPermissions.CollaborationGrantsAccept,WorkflowPermissions.CollaborationGrantsRevoke,
             "tenancy.tenants.switch"],password:FullNetApiFactory.TestPassword);
        var foreignEmail=foreignName+"@example.com";
        await factory.EnsureHostUserProfileEmailAsync(foreignIdentity.UserId,foreignEmail,foreignName);
        var foreignInvitation=await PostAsync(foreign,"/api/v1/identity/tenant-members/invitations",new CreateTenantInvitationRequest(foreignEmail,TenantMemberRoles.Member));
        using var foreignMember=factory.CreateClientForHost("localhost"); foreignMember.DefaultRequestHeaders.Add("Origin","http://localhost");
        var foreignHostToken=await IntegrationTestAuthHelper.LoginAsHostUserAsync(foreignMember,foreignName,FullNetApiFactory.TestPassword);
        foreignMember.DefaultRequestHeaders.Authorization=new("Bearer",foreignHostToken);
        await PostAsync(foreignMember,$"/api/v1/me/tenant-invitations/{foreignInvitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept",new { });
        foreignMember.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(foreignMember,foreignHostToken,foreignId));
        var foreignActor=foreignIdentity.UserId;
        var foreignAsset=await AssetAsync(foreignMember);
        await CaseAsync(factory,db,target,foreignMember,source,tenantAsset,foreignAsset,tenant.Id,foreignId,targetActor,foreignActor);
        await CaseAsync(factory,db,foreignMember,target,source,foreignAsset,tenantAsset,foreignId,tenant.Id,foreignActor,targetActor);
        await ExpirationAsync(db,source,target,hostAsset,tenantAsset,tenant.Id,targetActor);
        await DisabledPartyAsync(factory,source,target,foreignMember,hostAsset,tenantAsset,foreignAsset,tenant.Id,foreignId,targetActor);
        var denied=await factory.CreateHostIdentityAsync("collab.denied."+Guid.NewGuid().ToString("N"),[]);
        using var deniedClient=factory.CreateClientForHost("localhost"); deniedClient.DefaultRequestHeaders.Authorization=new("Bearer",denied.AccessToken);
        using(var response=await deniedClient.PostAsJsonAsync("/api/v1/workflow/collaboration-grants",new { })) Assert.AreEqual(HttpStatusCode.Forbidden,response.StatusCode);
        var unknown=Guid.CreateVersion7();
        using(var response=await deniedClient.GetAsync($"/api/v1/workflow/collaboration-grants/{unknown:D}")) Assert.AreEqual(HttpStatusCode.Forbidden,response.StatusCode);
        foreach(var action in new[]{"accept","revoke"})
        {
            using var response=await deniedClient.PostAsJsonAsync($"/api/v1/workflow/collaboration-grants/{unknown:D}/{action}",new { });
            Assert.AreEqual(HttpStatusCode.Forbidden,response.StatusCode);
        }
        var count=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant");
        var actions=await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant_action");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer ? "DROP INDEX ux_fn_workflow_collaboration_grant_proposal ON fn_workflow_collaboration_grant" : "ALTER TABLE fn_workflow_collaboration_grant DROP INDEX ux_fn_workflow_collaboration_grant_proposal");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer ? "ALTER TABLE fn_workflow_collaboration_grant DROP CONSTRAINT ck_fn_workflow_collaboration_grant_state" : "ALTER TABLE fn_workflow_collaboration_grant DROP CHECK ck_fn_workflow_collaboration_grant_state");
        await db.ExecuteAsync(provider==DatabaseProvider.SqlServer ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%330_WorkflowCollaborationGrants.sql'" : "DELETE FROM schemaversions WHERE ScriptName LIKE '%330_WorkflowCollaborationGrants.sql'");
        MigrateThrough330(provider,connection,1); MigrateThrough330(provider,connection,0);
        Assert.AreEqual(count,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant"));
        Assert.AreEqual(actions,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant_action"));
    }

    // 工厂已建立前置结构；恢复只允许330脚本，未来新增迁移不能进入本场景。
    private static void MigrateThrough330(DatabaseProvider provider,string connection,int expectedScripts)
    {
        var builder=provider==DatabaseProvider.SqlServer ? DeployChanges.To.SqlDatabase(connection)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connection,MySqlGuidStorageMode.Binary16,allowUserVariables:true));
        var result=builder.WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly,
            name=>name.Contains($".Migrations.{provider}.",StringComparison.Ordinal) && name.EndsWith(".330_WorkflowCollaborationGrants.sql",StringComparison.Ordinal))
            .WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
        Assert.IsTrue(result.Successful,result.Error?.ToString());
        Assert.HasCount(expectedScripts,result.Scripts);
    }

    private static async Task ExpirationAsync(DbConnection db,HttpClient source,HttpClient target,Asset sourceAsset,Asset targetAsset,Guid targetTenant,Guid targetActor)
    {
        const string path="/api/v1/workflow/collaboration-grants";
        var offer=new { schemaVersion=1,sourceDefinitionId=sourceAsset.DefinitionId,sourceFormVersionId=sourceAsset.FormVersionId,
            targetWorkflowKey=targetAsset.Key,inputFieldKeys=new[]{"title"},outputFieldKeys=new[]{"title"},maximumTargetActivations=64,expiresAtUtc=DateTimeOffset.UtcNow.AddHours(-1) };
        var proposal=new { targetTenantId=targetTenant,targetScopeKey="tenant",idempotencyKey="expired."+Guid.NewGuid().ToString("N"),offer };
        await PostAsync(source,path,proposal,HttpStatusCode.BadRequest);
        var id=(await PostAsync(source,path,proposal with { offer=offer with { expiresAtUtc=DateTimeOffset.UtcNow.AddHours(1) } })).GetProperty("id").GetGuid();
        // 在隔离库模拟时间已越过到期点，不等待真实时钟或放宽生产授权判断。
        await db.ExecuteAsync("UPDATE fn_workflow_collaboration_grant SET CreatedAtUtc=@created,ExpiresAtUtc=@expired WHERE Id=@id",
            new { created=DateTimeOffset.UtcNow.AddHours(-2),expired=DateTimeOffset.UtcNow.AddMinutes(-1),id });
        await PostAsync(target,$"{path}/{id:D}/accept",new { expectedRevision=1,idempotencyKey="expired-accept",acceptance=new {
            schemaVersion=1,targetDefinitionVersionId=targetAsset.VersionId,starterUserId=targetActor,maximumActivities=32,allowedUserIds=new[]{targetActor},
            inputFields=new Dictionary<string,string>{{"title","title"}},outputFields=new Dictionary<string,string>{{"title","title"}} } },HttpStatusCode.Conflict);
        await PostAsync(source,$"{path}/{id:D}/revoke",new { expectedRevision=1,idempotencyKey="expired-revoke",reason="到期仍允许收紧授权" });
        Assert.AreEqual(2,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_grant_action WHERE GrantId=@id",new { id }));
    }

    private static async Task DisabledPartyAsync(FullNetApiFactory factory,HttpClient host,HttpClient target,HttpClient foreign,
        Asset hostAsset,Asset targetAsset,Asset foreignAsset,Guid targetTenant,Guid foreignTenant,Guid targetActor)
    {
        const string path="/api/v1/workflow/collaboration-grants";
        var offer=new { schemaVersion=1,sourceDefinitionId=foreignAsset.DefinitionId,sourceFormVersionId=foreignAsset.FormVersionId,
            targetWorkflowKey=targetAsset.Key,inputFieldKeys=new[]{"title"},outputFieldKeys=new[]{"title"},maximumTargetActivations=64,expiresAtUtc=DateTimeOffset.UtcNow.AddHours(1) };
        var id=(await PostAsync(foreign,path,new { targetTenantId=targetTenant,targetScopeKey="tenant",idempotencyKey="disable-source",offer })).GetProperty("id").GetGuid();
        var administrator=await factory.CreateHostIdentityAsync("collab.lifecycle."+Guid.NewGuid().ToString("N"),[TenancyTenantManagementPermissions.Disable]);
        using var lifecycle=factory.CreateClientForHost("localhost"); lifecycle.DefaultRequestHeaders.Authorization=new("Bearer",administrator.AccessToken);
        await PostAsync(lifecycle,$"/api/v1/tenancy/tenants/{foreignTenant:D}/disable",new { });
        await PostAsync(target,$"{path}/{id:D}/accept",new { expectedRevision=1,idempotencyKey="disabled-accept",acceptance=new {
            schemaVersion=1,targetDefinitionVersionId=targetAsset.VersionId,starterUserId=targetActor,maximumActivities=32,allowedUserIds=new[]{targetActor},
            inputFields=new Dictionary<string,string>{{"title","title"}},outputFields=new Dictionary<string,string>{{"title","title"}} } },HttpStatusCode.Conflict);
        // 对方被禁用后，活动一方仍有权撤销已有同意；禁止发起新的协作。
        await PostAsync(target,$"{path}/{id:D}/revoke",new { expectedRevision=1,idempotencyKey="disabled-revoke",reason="对方已禁用" });
        await PostAsync(host,path,new { targetTenantId=foreignTenant,targetScopeKey="tenant",idempotencyKey="disabled-target",offer=offer with {
            sourceDefinitionId=hostAsset.DefinitionId,sourceFormVersionId=hostAsset.FormVersionId,targetWorkflowKey=foreignAsset.Key } },HttpStatusCode.BadRequest);
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
