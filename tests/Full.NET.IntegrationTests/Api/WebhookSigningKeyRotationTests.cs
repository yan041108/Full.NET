using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实双库轮换、审计、授权与 Worker；传输可控以冻结已经领取的一批投递。</summary>
[TestClass]
public sealed class WebhookSigningKeyRotationTests
{
    private static readonly string[] Rights=[WebhookPermissions.SubscriptionsRead,WebhookPermissions.SubscriptionsManage,WebhookPermissions.SigningKeysRotate,"tenancy.tenants.switch"];
    private const string OldSecret="rotation-old-protected-secret",NewSecret="rotation-new-protected-secret";

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Prepared_key_stays_inactive_until_explicit_activation_and_remaining_claimed_batch_uses_new_profile(DatabaseProvider provider)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));var token=timeout.Token;
        await using var f=await Fixture.CreateAsync(provider,token);await f.EnqueueAsync(token);await f.EnqueueAsync(token);
        var sending=f.ProcessAsync(token);await f.Receiver.Started.Task.WaitAsync(token);
        try
        {
            var request=new PrepareWebhookSigningKeyRotationRequest(Guid.CreateVersion7(),f.Subscription.Version,NewSecret);
            var prepared=await f.PrepareAsync(request,token);Assert.AreEqual("prepared",prepared.StateKey);Assert.AreNotEqual(prepared.PreviousKeyId,prepared.SigningKeyId);
            var current=await f.CurrentAsync(token);Assert.AreEqual(f.Subscription.SigningKeyId,current.SigningKeyId);Assert.AreEqual(2,current.Version);
            Assert.AreEqual(prepared,await f.PrepareAsync(request,token));
            using(var wrong=await f.Client.PostAsJsonAsync(f.Path,request with{SigningSecret="different-protected-secret"},token))Assert.AreEqual(HttpStatusCode.Conflict,wrong.StatusCode);
            using(var competing=await f.Client.PostAsJsonAsync(f.Path,request with{OperationId=Guid.CreateVersion7(),Version=current.Version},token))Assert.AreEqual(HttpStatusCode.Conflict,competing.StatusCode);
            var complete=new CompleteWebhookSigningKeyRotationRequest(current.Version,prepared.SigningKeyId);
            using(var wrongKey=await f.Client.PostAsJsonAsync(f.Path+"/"+prepared.OperationId+"/activate",complete with{SigningKeyId=Guid.CreateVersion7()},token))Assert.AreEqual(HttpStatusCode.Conflict,wrongKey.StatusCode);
            var activated=await f.CompleteAsync(prepared.OperationId,complete,true,token);Assert.AreEqual("activated",activated.StateKey);Assert.AreEqual(3,activated.CompletedVersion);
            Assert.AreEqual(activated,await f.CompleteAsync(prepared.OperationId,complete,true,token));Assert.AreEqual(activated,await f.PrepareAsync(request,token));
            current=await f.CurrentAsync(token);Assert.AreEqual(prepared.SigningKeyId,current.SigningKeyId);Assert.IsTrue(current.IsActive);
            Assert.AreEqual(0L,await f.Db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_key_rotation WHERE ProtectedSecret IS NOT NULL"));
            Assert.IsNull(await f.Db.ExecuteScalarAsync<Guid?>("SELECT PreparedRotationId FROM fn_webhooks_subscription WHERE Id=@Id",new{Id=current.Id}));
        }
        finally{f.Receiver.Release.TrySetResult();}
        Assert.AreEqual(2,await sending);Assert.AreEqual(2,f.Receiver.Items.Count);
        var first=f.Receiver.Items[0];var second=f.Receiver.Items[1];Assert.AreEqual(f.Subscription.SigningKeyId,first.Stamp.KeyId);Assert.AreEqual((await f.CurrentAsync(token)).SigningKeyId,second.Stamp.KeyId);
        Assert.AreEqual(WebhookSignatureHelper.ComputeSignature(OldSecret,first.Body,first.Stamp),first.Signature);Assert.AreEqual(WebhookSignatureHelper.ComputeSignature(NewSecret,second.Body,second.Stamp),second.Signature);
        Assert.AreEqual(2L,await f.Db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery WHERE Status='Delivered' AND AttemptCount=1"));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Concurrent_prepares_reserve_once_and_authorized_cancellation_keeps_old_key_and_cross_tenant_boundaries(DatabaseProvider provider)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));var token=timeout.Token;await using var f=await Fixture.CreateAsync(provider,token);
        var requests=new[]{new PrepareWebhookSigningKeyRotationRequest(Guid.CreateVersion7(),1,NewSecret),new PrepareWebhookSigningKeyRotationRequest(Guid.CreateVersion7(),1,NewSecret)};
        var results=await Task.WhenAll(requests.Select(request=>f.Client.PostAsJsonAsync(f.Path,request,token)));
        try{Assert.AreEqual(1,results.Count(r=>r.StatusCode==HttpStatusCode.OK));Assert.AreEqual(1,results.Count(r=>r.StatusCode==HttpStatusCode.Conflict));}finally{foreach(var result in results)result.Dispose();}
        var prepared=(await f.Client.GetFromJsonAsync<WebhookSigningKeyRotationReceipt>(f.Path+"/pending",token))!;
        Assert.AreEqual(1L,await f.Db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_key_rotation"));
        foreach(var rights in new[]{new[]{WebhookPermissions.SubscriptionsRead},new[]{WebhookPermissions.SigningKeysRotate}})
        {
            using var limited=f.Factory.CreateClientForHost("acme.localhost");limited.DefaultRequestHeaders.Authorization=new("Bearer",await f.AccessAsync(f.Subscription.TenantId,rights,token));
            using var denied=await limited.PostAsJsonAsync(f.Path+"/"+prepared.OperationId+"/cancel",new CompleteWebhookSigningKeyRotationRequest(2,prepared.SigningKeyId),token);Assert.AreEqual(HttpStatusCode.Forbidden,denied.StatusCode);
        }
        using(var foreign=f.Factory.CreateClientForHost("acme.localhost"))
        {
            foreign.DefaultRequestHeaders.Authorization=new("Bearer",await f.AccessAsync(Guid.CreateVersion7(),Rights,token,createTenant:true));
            foreign.DefaultRequestHeaders.Host=f.CreatedTenantDomain;
            using var read=await foreign.GetAsync(f.Path+"/pending",token);Assert.AreEqual(HttpStatusCode.NotFound,read.StatusCode);
            using var write=await foreign.PostAsJsonAsync(f.Path,requests[0],token);Assert.AreEqual(HttpStatusCode.NotFound,write.StatusCode);
        }
        // 准备人离开后，另一个持有精确轮换权限的当前操作者仍可明确取消，不能留下永久占位。
        using var other=f.Factory.CreateClientForHost("acme.localhost");other.DefaultRequestHeaders.Authorization=new("Bearer",await f.AccessAsync(f.Subscription.TenantId,Rights,token));
        var completion=new CompleteWebhookSigningKeyRotationRequest(2,prepared.SigningKeyId);
        using(var cancelled=await other.PostAsJsonAsync(f.Path+"/"+prepared.OperationId+"/cancel",completion,token))
        {Assert.AreEqual(HttpStatusCode.OK,cancelled.StatusCode);Assert.AreEqual("cancelled",(await cancelled.Content.ReadFromJsonAsync<WebhookSigningKeyRotationReceipt>(token))!.StateKey);}
        Assert.AreEqual(f.Subscription.SigningKeyId,(await f.CurrentAsync(token)).SigningKeyId);
        Assert.AreEqual(0L,await f.Db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_key_rotation WHERE ProtectedSecret IS NOT NULL"));
        Assert.AreEqual(1L,await f.Db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_key_rotation WHERE CompletedByUserId<>ActorUserId AND CompletedBySessionId IS NOT NULL"));
        var next=await f.PrepareAsync(new(Guid.CreateVersion7(),3,"another-new-protected-secret"),token);Assert.AreEqual("prepared",next.StateKey);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer,false)]
    [DataRow(DatabaseProvider.MySql,false)]
    [DataRow(DatabaseProvider.SqlServer,true)]
    [DataRow(DatabaseProvider.MySql,true)]
    public async Task Audit_failure_or_actual_session_expiry_before_commit_rolls_back_active_key_and_pending_secret(DatabaseProvider provider,bool expiry)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));var token=timeout.Token;await using var f=await Fixture.CreateAsync(provider,token);
        var prepared=await f.PrepareAsync(new(Guid.CreateVersion7(),1,NewSecret),token);var before=await f.CurrentAsync(token);
        const string constraint="CK_fn_webhooks_key_rotation_TestPrepared";
        if(expiry)f.ExpireAtCommit=true;else await f.Db.ExecuteAsync($"ALTER TABLE fn_webhooks_key_rotation ADD CONSTRAINT {constraint} CHECK (StateKey='prepared')");
        try{using var result=await f.Client.PostAsJsonAsync(f.Path+"/"+prepared.OperationId+"/activate",new CompleteWebhookSigningKeyRotationRequest(2,prepared.SigningKeyId),token);Assert.AreEqual(expiry?HttpStatusCode.Forbidden:HttpStatusCode.InternalServerError,result.StatusCode);}
        finally{f.Clock.Offset=TimeSpan.Zero;if(!expiry)await f.Db.ExecuteAsync(provider==DatabaseProvider.SqlServer?$"ALTER TABLE fn_webhooks_key_rotation DROP CONSTRAINT {constraint}":$"ALTER TABLE fn_webhooks_key_rotation DROP CHECK {constraint}");}
        Assert.AreEqual(before,await f.CurrentAsync(token));Assert.AreEqual(prepared,await f.Client.GetFromJsonAsync<WebhookSigningKeyRotationReceipt>(f.Path+"/pending",token));
        Assert.AreEqual(1L,await f.Db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_key_rotation WHERE StateKey='prepared' AND ProtectedSecret IS NOT NULL AND CompletedByUserId IS NULL"));
    }

    private sealed class Clock:IClock{internal TimeSpan Offset;public DateTimeOffset UtcNow=>DateTimeOffset.UtcNow+Offset;}
    private sealed class ExpiringAuthorization(IIndependentCurrentSessionAuthorization actual,Action before):IIndependentCurrentSessionAuthorization
    {public bool HasIndependentReadCapacity=>actual.HasIndependentReadCapacity;public Task<AuthorizedSessionActor?> AuthorizeAsync(string permission,CancellationToken token){before();return actual.AuthorizeAsync(permission,token);}}
    private sealed record Sent(string Body,string Signature,WebhookSignatureStamp Stamp);
    private sealed class Receiver:IHttpClientFactory
    {
        internal readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);internal readonly List<Sent> Items=[];
        public HttpClient CreateClient(string name)=>new(new Handler(async(request,token)=>
        {var stamp=new WebhookSignatureStamp(Guid.Parse(request.Headers.GetValues("X-FullNet-Key-Id").Single()),Guid.Parse(request.Headers.GetValues("X-FullNet-Event-Id").Single()),Guid.Parse(request.Headers.GetValues("X-FullNet-Delivery-Id").Single()),request.Headers.GetValues("X-FullNet-Event-Type").Single(),long.Parse(request.Headers.GetValues("X-FullNet-Timestamp").Single(),System.Globalization.CultureInfo.InvariantCulture));
            Items.Add(new(await request.Content!.ReadAsStringAsync(token),request.Headers.GetValues("X-FullNet-Signature").Single(),stamp));if(Items.Count==1){Started.TrySetResult();await Release.Task.WaitAsync(token);}return new HttpResponseMessage(HttpStatusCode.OK);}));
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
    private sealed class Fixture:IAsyncDisposable
    {
        internal FullNetApiFactory Factory=null!;internal HttpClient Client=null!;internal DbConnection Db=null!;internal WebhookSubscriptionResponse Subscription=null!;internal string Path="";internal string? CreatedTenantDomain;internal readonly Clock Clock=new();internal readonly Receiver Receiver=new();internal bool ExpireAtCommit;
        internal static async Task<Fixture> CreateAsync(DatabaseProvider provider,CancellationToken token)
        {
            var f=new Fixture();try{
            var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
            f.Factory=new(provider,cs,configureTestServices:services=>{services.RemoveAll<IClock>();services.AddSingleton<IClock>(f.Clock);services.RemoveAll<IHttpClientFactory>();services.AddSingleton<IHttpClientFactory>(f.Receiver);
                services.AddScoped<IndependentCurrentSessionAuthorization>();services.RemoveAll<IIndependentCurrentSessionAuthorization>();services.AddScoped<IIndependentCurrentSessionAuthorization>(sp=>new ExpiringAuthorization(sp.GetRequiredService<IndependentCurrentSessionAuthorization>(),()=>{if(f.ExpireAtCommit){f.ExpireAtCommit=false;f.Clock.Offset=TimeSpan.FromDays(2);}}));});
            await f.Factory.InitializeAsync(token);f.Client=f.Factory.CreateClientForHost("acme.localhost");var tenant=await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(f.Client,token);f.Client.DefaultRequestHeaders.Authorization=new("Bearer",await f.AccessAsync(tenant.Id,Rights,token));
            using var response=await f.Client.PostAsJsonAsync("/api/v1/webhooks/subscriptions",new CreateWebhookSubscriptionRequest("fullnet.workflow.instance.completed","https://93.184.216.34/rotating",OldSecret),token);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);f.Subscription=(await response.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token))!;
            f.Path=$"/api/v1/webhooks/subscriptions/{f.Subscription.Id:D}/signing-key-rotations";f.Db=provider==DatabaseProvider.SqlServer?new SqlConnection(cs):ReviewFixMigrationRecoverySupport.MySqlConnection(cs);await f.Db.OpenAsync(token);return f;
            }catch{await f.DisposeAsync();throw;}
        }
        internal async Task<string> AccessAsync(Guid tenantId,string[] rights,CancellationToken token,bool createTenant=false)
        {
            using var host=Factory.CreateClientForHost("localhost");var access=await Factory.CreateHostAccessTokenAsync([..rights,"tenancy.tenants.switch","tenancy.tenants.manage"],token);
            if(createTenant){await using var scope=Factory.Services.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();var key="rotation-"+Guid.CreateVersion7().ToString("N");CreatedTenantDomain=key+".localhost";var result=await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(new(key,"Rotation other",CreatedTenantDomain),token);Assert.IsTrue(result.IsSuccess);tenantId=result.Value!.Id;}
            return await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host,access,tenantId,token);
        }
        internal async Task<WebhookSubscriptionResponse> CurrentAsync(CancellationToken token)=>(await Client.GetFromJsonAsync<WebhookSubscriptionResponse[]>("/api/v1/webhooks/subscriptions",token))!.Single(row=>row.Id==Subscription.Id);
        internal async Task<WebhookSigningKeyRotationReceipt> PrepareAsync(PrepareWebhookSigningKeyRotationRequest request,CancellationToken token){using var response=await Client.PostAsJsonAsync(Path,request,token);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode,await response.Content.ReadAsStringAsync(token));return(await response.Content.ReadFromJsonAsync<WebhookSigningKeyRotationReceipt>(token))!;}
        internal async Task<WebhookSigningKeyRotationReceipt> CompleteAsync(Guid operation,CompleteWebhookSigningKeyRotationRequest request,bool activate,CancellationToken token){using var response=await Client.PostAsJsonAsync(Path+"/"+operation+(activate?"/activate":"/cancel"),request,token);Assert.AreEqual(HttpStatusCode.OK,response.StatusCode,await response.Content.ReadAsStringAsync(token));return(await response.Content.ReadFromJsonAsync<WebhookSigningKeyRotationReceipt>(token))!;}
        internal async Task EnqueueAsync(CancellationToken token){await using var scope=Factory.Services.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();var id=Guid.CreateVersion7();var envelope=new WebhookEventEnvelope(Subscription.EventType,id,Subscription.TenantId,Clock.UtcNow,new(Guid.CreateVersion7(),Guid.CreateVersion7(),"rotation","fixture"));await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellation=>{await scope.ServiceProvider.GetRequiredService<WebhookEventEnqueueService>().EnqueueAsync(Subscription.TenantId,Subscription.EventType,id,envelope,cancellation);return 0;},token);}
        internal async Task<int> ProcessAsync(CancellationToken token){await using var scope=Factory.Services.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();return await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ProcessPendingAsync(token);}
        public async ValueTask DisposeAsync(){Receiver.Release.TrySetResult();Client?.Dispose();if(Db is not null)await Db.DisposeAsync();Factory?.Dispose();}
    }
}
