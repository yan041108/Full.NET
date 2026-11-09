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

        await VerifyTenantPauseAsync(f, token);
    }

    private static async Task VerifyTenantPauseAsync(Fixture f, CancellationToken token)
    {
        await f.EnqueueAsync(token);
        _ = await f.AccessAsync(Guid.NewGuid(), Rights, token, createTenant: true);
        using var host = f.Factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await f.Factory.CreateHostAccessTokenAsync(
            ["tenancy.tenant_lifecycle.read", "tenancy.tenant_lifecycle.suspend", "tenancy.tenant_lifecycle.reactivate"], token));
        var path = $"/api/v1/tenancy/tenants/{f.Subscription.TenantId:D}";
        var active = await host.GetFromJsonAsync<TenantSummary>(path + "/lifecycle", token);
        Assert.IsNotNull(active);
        using var paused = await host.PostAsJsonAsync(path + "/suspend", new { active.Version }, token);
        Assert.AreEqual(HttpStatusCode.OK, paused.StatusCode, await paused.Content.ReadAsStringAsync(token));
        var inactive = await paused.Content.ReadFromJsonAsync<TenantSummary>(token);
        Assert.IsNotNull(inactive);

        Assert.AreEqual(1, await f.ProcessAsync(token));
        Assert.HasCount(2, f.Receiver.Items);
        Assert.AreEqual(0, await f.ProcessAsync(token));
        var original = await f.Db.QuerySingleAsync<(Guid Id, Guid EventId)>(
            "SELECT Id, EventId FROM fn_webhooks_delivery WHERE LOWER(Status)='pending' AND AttemptCount=0 AND LastError=@Code",
            new { Code = WebhookDeliveryBatchProcessor.TenantInactive });

        using var recovered = await host.PostAsJsonAsync(path + "/reactivate", new ReactivateTenantRequest(inactive.Version), token);
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync(token));
        f.Clock.Offset = TimeSpan.FromMinutes(6);
        Assert.AreEqual(1, await f.ProcessAsync(token));
        Assert.HasCount(3, f.Receiver.Items);
        Assert.AreEqual(original.Id, f.Receiver.Items[^1].Stamp.DeliveryId);
        Assert.AreEqual(original.EventId, f.Receiver.Items[^1].Stamp.EventId);
        Assert.AreEqual(1, await f.Db.ExecuteScalarAsync<int>("SELECT AttemptCount FROM fn_webhooks_delivery WHERE Id=@Id", new { original.Id }));
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Claimed_webhook_honors_tenant_pause_without_holding_another_tenant(DatabaseProvider provider)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = deadline.Token;
        var gate = new TenantActivityGate();
        await using var f = await Fixture.CreateAsync(provider, token, gate);
        gate.TargetTenantId = f.Subscription.TenantId;
        var originalEventId = await f.EnqueueAsync(token);

        var otherAccess = await f.AccessAsync(Guid.CreateVersion7(), Rights, token, createTenant: true);
        using var other = f.Factory.CreateClientForHost(f.CreatedTenantDomain!);
        other.DefaultRequestHeaders.Authorization = new("Bearer", otherAccess);
        using var created = await other.PostAsJsonAsync("/api/v1/webhooks/subscriptions",
            new CreateWebhookSubscriptionRequest(f.Subscription.EventType, "https://93.184.216.34/other", OldSecret), token);
        Assert.AreEqual(HttpStatusCode.OK, created.StatusCode, await created.Content.ReadAsStringAsync(token));
        var otherSubscription = await created.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token);
        Assert.IsNotNull(otherSubscription);
        var otherEventId = await f.EnqueueAsync(token, otherSubscription);

        using var host = f.Factory.CreateClientForHost("localhost");
        host.DefaultRequestHeaders.Authorization = new("Bearer", await f.Factory.CreateHostAccessTokenAsync(
            [TenancyTenantLifecyclePermissions.Read, TenancyTenantLifecyclePermissions.Suspend,
                TenancyTenantLifecyclePermissions.Reactivate, TenancyTenantLifecyclePermissions.Close], token));
        var path = $"/api/v1/tenancy/tenants/{f.Subscription.TenantId:D}";
        var active = await host.GetFromJsonAsync<TenantSummary>(path + "/lifecycle", token);
        Assert.IsNotNull(active);
        f.Receiver.Release.TrySetResult();
        var processing = f.ProcessAsync(token);
        TenantSummary inactive;
        Guid deliveryId;
        try
        {
            await gate.Entered.Task.WaitAsync(token);
            var claimed = await f.Db.QuerySingleAsync<(Guid Id, string Status, int AttemptCount)>(
                "SELECT Id, Status, AttemptCount FROM fn_webhooks_delivery WHERE EventId=@EventId",
                new { EventId = originalEventId });
            Assert.AreEqual("processing", claimed.Status.ToLowerInvariant());
            Assert.AreEqual(0, claimed.AttemptCount);
            deliveryId = claimed.Id;
            using var paused = await host.PostAsJsonAsync(path + "/suspend", new SuspendTenantRequest(active.Version), token);
            Assert.AreEqual(HttpStatusCode.OK, paused.StatusCode, await paused.Content.ReadAsStringAsync(token));
            inactive = (await paused.Content.ReadFromJsonAsync<TenantSummary>(token))!;
            Assert.IsFalse(inactive.IsActive);
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        Assert.AreEqual(2, await processing);
        Assert.AreEqual(otherEventId, f.Receiver.Items.Single().Stamp.EventId);
        var deferred = await f.Db.QuerySingleAsync<(string Status, int AttemptCount, string LastError)>(
            "SELECT Status, AttemptCount, LastError FROM fn_webhooks_delivery WHERE Id=@Id", new { Id = deliveryId });
        Assert.AreEqual("pending", deferred.Status.ToLowerInvariant());
        Assert.AreEqual(0, deferred.AttemptCount);
        Assert.AreEqual(WebhookDeliveryBatchProcessor.TenantInactive, deferred.LastError);

        using var recovered = await host.PostAsJsonAsync(path + "/reactivate", new ReactivateTenantRequest(inactive.Version), token);
        Assert.AreEqual(HttpStatusCode.OK, recovered.StatusCode, await recovered.Content.ReadAsStringAsync(token));
        f.Clock.Offset = TimeSpan.FromMinutes(6);
        Assert.AreEqual(1, await f.ProcessAsync(token));
        Assert.HasCount(2, f.Receiver.Items);
        Assert.AreEqual(originalEventId, f.Receiver.Items[1].Stamp.EventId);
        Assert.AreEqual(deliveryId, f.Receiver.Items[1].Stamp.DeliveryId);

        var closingEventId = await f.EnqueueAsync(token);
        var activeAgain = await recovered.Content.ReadFromJsonAsync<TenantSummary>(token);
        Assert.IsNotNull(activeAgain);
        using var closed = await host.PostAsJsonAsync(path + "/close", new CloseTenantRequest(activeAgain.Version), token);
        Assert.AreEqual(HttpStatusCode.OK, closed.StatusCode, await closed.Content.ReadAsStringAsync(token));
        Assert.AreEqual(1, await f.ProcessAsync(token));
        var ended = await f.Db.QuerySingleAsync<(Guid Id, string Status, int AttemptCount, string LastError, int IsTerminal)>(
            """
            SELECT Id, Status, AttemptCount, LastError, CASE WHEN NextAttemptAtUtc IS NULL THEN 1 ELSE 0 END
            FROM fn_webhooks_delivery WHERE EventId=@EventId
            """, new { EventId = closingEventId });
        Assert.AreEqual("failed", ended.Status.ToLowerInvariant());
        Assert.AreEqual(0, ended.AttemptCount);
        Assert.AreEqual(1, ended.IsTerminal);
        Assert.AreEqual(WebhookDeliveryBatchProcessor.TenantClosed, ended.LastError);
        Assert.AreEqual(WebhookDeliveryBatchProcessor.TenantClosed, await f.Db.ExecuteScalarAsync<string>(
            "SELECT ErrorCode FROM fn_webhooks_delivery_attempt WHERE DeliveryId=@Id", new { ended.Id }));
        f.Clock.Offset = TimeSpan.FromMinutes(12);
        Assert.AreEqual(0, await f.ProcessAsync(token));
        Assert.HasCount(2, f.Receiver.Items);
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
    private sealed class TenantActivityGate
    {
        internal Guid TargetTenantId;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int paused;

        internal async Task<TenantActivityState> CheckAsync(ITenantActivityReadPort inner, Guid tenantId, CancellationToken token)
        {
            if (tenantId == TargetTenantId && Interlocked.Exchange(ref paused, 1) == 0)
            {
                // 只冻结第一次权威查询；真实领取已提交，暂停后仍由正式查询决定是否外发。
                Entered.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return await inner.ReadActivityAsync(tenantId, token);
        }
    }

    private sealed class GatedTenantActivity(ITenantActivityReadPort inner, TenantActivityGate gate) : ITenantActivityReadPort
    {
        public Task<bool> IsActiveTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            inner.IsActiveTenantAsync(tenantId, cancellationToken);

        public Task<TenantActivityState> ReadActivityAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            gate.CheckAsync(inner, tenantId, cancellationToken);
    }

    private sealed class ExpiringAuthorization(IIndependentCurrentSessionAuthorization actual,Action before):IIndependentCurrentSessionAuthorization
    {
        public bool HasIndependentReadCapacity=>actual.HasIndependentReadCapacity;
        public Task<AuthorizedSessionActor?> AuthorizeAsync(string permission,CancellationToken token){before();return actual.AuthorizeAsync(permission,token);}
        public Task<AuthorizedSessionActor?> AuthorizeAllAsync(IReadOnlyCollection<string> permissions,CancellationToken token=default){before();return actual.AuthorizeAllAsync(permissions,token);}
    }
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
        internal static async Task<Fixture> CreateAsync(DatabaseProvider provider,CancellationToken token, TenantActivityGate? activityGate = null)
        {
            var f=new Fixture();try{
            var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
            f.Factory=new(provider,cs,configureTestServices:services=>{services.RemoveAll<IClock>();services.AddSingleton<IClock>(f.Clock);services.RemoveAll<IHttpClientFactory>();services.AddSingleton<IHttpClientFactory>(f.Receiver);
                services.AddScoped<IndependentCurrentSessionAuthorization>();services.RemoveAll<IIndependentCurrentSessionAuthorization>();services.AddScoped<IIndependentCurrentSessionAuthorization>(sp=>new ExpiringAuthorization(sp.GetRequiredService<IndependentCurrentSessionAuthorization>(),()=>{if(f.ExpireAtCommit){f.ExpireAtCommit=false;f.Clock.Offset=TimeSpan.FromDays(2);}}));
                if (activityGate is not null)
                {
                    var actual = services.Single(item => item.ServiceType == typeof(ITenantActivityReadPort));
                    services.Remove(actual);
                    services.AddScoped<ITenantActivityReadPort>(sp => new GatedTenantActivity((ITenantActivityReadPort)actual.ImplementationFactory!(sp), activityGate));
                }
            });
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
        internal async Task<Guid> EnqueueAsync(CancellationToken token, WebhookSubscriptionResponse? target = null)
        {
            var subscription = target ?? Subscription;
            await using var scope = Factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var id = Guid.CreateVersion7();
            var envelope = new WebhookEventEnvelope(subscription.EventType, id, subscription.TenantId, Clock.UtcNow,
                new(Guid.CreateVersion7(), Guid.CreateVersion7(), "rotation", "fixture"));
            await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellation =>
            {
                await scope.ServiceProvider.GetRequiredService<WebhookEventEnqueueService>()
                    .EnqueueAsync(subscription.TenantId, subscription.EventType, id, envelope, cancellation);
                return 0;
            }, token);
            return id;
        }
        internal async Task<int> ProcessAsync(CancellationToken token){await using var scope=Factory.Services.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();return await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ProcessPendingAsync(token);}
        public async ValueTask DisposeAsync(){Receiver.Release.TrySetResult();Client?.Dispose();if(Db is not null)await Db.DisposeAsync();Factory?.Dispose();}
    }
}
