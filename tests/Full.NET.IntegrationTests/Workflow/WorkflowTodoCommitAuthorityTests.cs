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
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>入口已授权后、待办事务持锁时撤销操作者，不能提交旧凭据的状态推进。</summary>
[TestClass]
public sealed class WorkflowTodoCommitAuthorityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer,"session")][DataRow(DatabaseProvider.MySql,"session")]
    [DataRow(DatabaseProvider.SqlServer,"api_key")][DataRow(DatabaseProvider.MySql,"api_key")]
    [DataRow(DatabaseProvider.SqlServer,"membership")][DataRow(DatabaseProvider.MySql,"membership")]
    public async Task Credential_revoked_after_preparation_cannot_commit_the_todo(DatabaseProvider provider,string credential)
    {
        var connection=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var window=new RevocationWindow();
        using var factory=new FullNetApiFactory(provider,connection,configureTestServices:services=>{
            var original=services.Last(descriptor=>descriptor.ServiceType==typeof(ICommandExecutor));services.Remove(original);
            services.AddScoped<ICommandExecutor>(sp=>new AfterLockCommand((ICommandExecutor)(original.ImplementationInstance
                ??original.ImplementationFactory?.Invoke(sp)??ActivatorUtilities.CreateInstance(sp,original.ImplementationType!)),window));
        });
        await factory.InitializeAsync();await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(connection)
            :new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));await db.OpenAsync();
        using var owner=factory.CreateClientForHost("localhost");owner.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner,"admin"));
        // 普通成员不授予 Host 全局企业切换权，否则移除成员关系后仍有独立准入。
        var username="commit-"+Guid.CreateVersion7().ToString("N");var actor=await factory.CreateHostIdentityAsync(username,[WorkflowPermissions.TodosRead,WorkflowPermissions.TodosApprove],password:FullNetApiFactory.TestPassword);
        using var client=factory.CreateClientForHost("localhost");client.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestAuthHelper.LoginAsHostUserAsync(client,username,FullNetApiFactory.TestPassword));
        Guid? tenantId=null;CreateHostApiKeyResponse? key=null;
        if(credential=="api_key"){key=await CreateKeyAsync(owner,actor.UserId);client.DefaultRequestHeaders.Authorization=new("ApiKey",key.Secret);}
        if(credential=="membership")
        {
            var ownerToken=owner.DefaultRequestHeaders.Authorization!.Parameter!;var tenant=(await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item=>item.Identifier=="acme");tenantId=tenant.Id;
            owner.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner,ownerToken,tenant.Id));
            var email=username+"@example.com";await factory.EnsureHostUserProfileEmailAsync(actor.UserId,email,username);
            client.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestAuthHelper.LoginAsHostUserAsync(client,username,FullNetApiFactory.TestPassword));
            var invitation=await PostAsync(owner,"/api/v1/identity/tenant-members/invitations",new CreateTenantInvitationRequest(email,TenantMemberRoles.Member));
            await PostAsync(client,"/api/v1/identity/tenant-invitations/accept",new AcceptTenantInvitationRequest(invitation.GetProperty("invitationToken").GetString()!));
            client.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client,client.DefaultRequestHeaders.Authorization!.Parameter!,tenant.Id));
        }
        var form=await PostAsync(owner,"/api/v1/workflow/forms",new{formKey="commit."+Guid.CreateVersion7().ToString("N"),draft=new{schemaVersion=1,adapterVersion=1,
            sections=new[]{new{sectionKey="main",fields=new[]{new{fieldKey="title",fieldTypeKey="text",required=false,constraints=new{}}}}}}},HttpStatusCode.Created);
        var formVersion=await PostAsync(owner,$"/api/v1/workflow/forms/{form.GetProperty("id").GetGuid():D}/publish",new{expectedRevision=1});
        var definition=await PostAsync(owner,"/api/v1/workflow/definitions",new{definitionKey="commit."+Guid.CreateVersion7().ToString("N"),draft=new{schemaVersion=1,workflowKindKey="approval",nodes=new[]{
            Node("start","start",new{nextNodeKeys=new[]{"review"}}),Node("review","human.approval",new{nextNodeKeys=new[]{"end"},assigneePolicy=new{sources=new[]{new{resolverKindKey="specified_users",userIds=new[]{actor.UserId}}}}}),Node("end","end",new{})}}},HttpStatusCode.Created);
        var published=await PostAsync(owner,$"/api/v1/workflow/definitions/{definition.GetProperty("id").GetGuid():D}/publish",new{expectedRevision=1,formVersionId=formVersion.GetProperty("id").GetGuid()});
        var started=await PostAsync(owner,"/api/v1/workflow/instances",new{definitionVersionId=published.GetProperty("id").GetGuid(),businessType="authority",businessId=Guid.CreateVersion7().ToString("N"),initialValues=new{title="Original"},idempotencyKey=Guid.CreateVersion7().ToString("N")},HttpStatusCode.Created);
        var instance=started.GetProperty("id").GetGuid();var todo=await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND AssigneeUserId=@actor",new{instance,actor=actor.UserId});
        window.Revoke=async()=>{
            var changed=credential switch {
                "api_key"=>await db.ExecuteAsync("UPDATE fn_identity_api_key SET IsActive=@Active WHERE Id=@Id",new{Active=false,Id=key!.Key.Id}),
                "membership"=>await db.ExecuteAsync("UPDATE fn_identity_tenant_member SET Status='Removed',Version=Version+1 WHERE UserId=@UserId AND TenantId=@TenantId AND Status='Active'",new{UserId=actor.UserId,TenantId=tenantId}),
                _=>await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=@Active WHERE Id=@Id",new{Active=false,Id=actor.UserId})};Assert.AreEqual(1,changed);
        };window.Armed=1;
        var command=new{expectedRevision=1,expectedFormRevision=1,fieldPatch=new{},comment="Original command",idempotencyKey=Guid.CreateVersion7().ToString("N")};
        using var denied=await client.PostAsJsonAsync($"/api/v1/workflow/todos/{todo:D}/approve",command);
        Assert.AreEqual(1,window.Hits,"必须在办理事务已经持锁后触发，不能用入口拒绝代替提交前保护。");
        Assert.AreEqual(HttpStatusCode.Forbidden,denied.StatusCode,await denied.Content.ReadAsStringAsync());
        Assert.AreEqual("active",await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@Id",new{Id=todo}));
        Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_instance WHERE Id=@Id",new{Id=instance}));
        Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>("SELECT Revision FROM fn_workflow_form_submission WHERE InstanceId=@Id",new{Id=instance}));
        Assert.AreEqual(0,await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_action_record WHERE InstanceId=@Id AND IdempotencyKey=@Key",new{Id=instance,Key=command.idempotencyKey}));
        if(credential=="membership")return;
        if(credential=="api_key"){var replacement=await CreateKeyAsync(owner,actor.UserId);client.DefaultRequestHeaders.Authorization=new("ApiKey",replacement.Secret);}
        else {await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=@Active WHERE Id=@Id",new{Active=true,Id=actor.UserId});client.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestAuthHelper.LoginAsHostUserAsync(client,username,FullNetApiFactory.TestPassword));}
        await PostAsync(client,$"/api/v1/workflow/todos/{todo:D}/approve",command);
        Assert.AreEqual("completed",await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@Id",new{Id=todo}));
    }

    private static object Node(string key,string kind,object config)=>new{nodeKey=key,nodeTypeKey=kind,nodeSchemaVersion=1,config};
    private static async Task<CreateHostApiKeyResponse> CreateKeyAsync(HttpClient owner,Guid userId)
    {using var response=await owner.PostAsJsonAsync("/api/v1/identity/api-keys",new CreateHostApiKeyRequest(userId,"Todo authority",[WorkflowPermissions.TodosRead,WorkflowPermissions.TodosApprove],null));Assert.AreEqual(HttpStatusCode.Created,response.StatusCode);return(await response.Content.ReadFromJsonAsync<CreateHostApiKeyResponse>())!;}
    private static async Task<JsonElement> PostAsync(HttpClient client,string path,object body,HttpStatusCode expected=HttpStatusCode.OK)
    {using var response=await client.PostAsJsonAsync(path,body);var json=await response.Content.ReadAsStringAsync();Assert.AreEqual(expected,response.StatusCode,json);return JsonSerializer.Deserialize<JsonElement>(json);}
    private sealed class RevocationWindow {public int Armed;public int Hits;public Func<Task> Revoke=()=>Task.CompletedTask;}
    private sealed class AfterLockCommand(ICommandExecutor inner,RevocationWindow window):ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement,object? parameters=null,CancellationToken cancellationToken=default)
        {var result=await inner.ExecuteAsync(statement,parameters,cancellationToken);if(result==1&&ReferenceEquals(statement,WorkflowSql.LockInstanceForTodoAction)&&Interlocked.Exchange(ref window.Armed,0)==1){window.Hits++;await window.Revoke();}return result;}
    }
}
