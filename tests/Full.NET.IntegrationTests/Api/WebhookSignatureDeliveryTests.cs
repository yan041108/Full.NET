using System.Net;
using System.Net.Http.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实双库领取使用公开 KeyId，每次重试刷新签名时间且保留原业务事件与正文。</summary>
[TestClass]
public sealed class WebhookSignatureDeliveryTests
{
    /// <summary>新必填身份不能破坏既有批量种子；只执行直接受影响的 Webhook 场景。</summary>
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Current_schema_preserves_existing_bounded_batch_seed(DatabaseProvider provider)
    {
        var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory=new FullNetApiFactory(provider,cs);await factory.InitializeAsync();
        await using var scope=factory.Services.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        await PerformanceWebhookBatchAssertions.VerifyAsync(scope.ServiceProvider,provider,_=>{});
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Retry_refreshes_signed_delivery_time_without_replacing_event_payload_or_key_identity(DatabaseProvider provider)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(120));var token=timeout.Token;var clock=new Clock();var receiver=new Receiver();
        var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory=new FullNetApiFactory(provider,cs,configureTestServices:services=>{services.RemoveAll<IClock>();services.AddSingleton<IClock>(clock);services.RemoveAll<IHttpClientFactory>();services.AddSingleton<IHttpClientFactory>(receiver);});
        await factory.InitializeAsync(token);using var client=factory.CreateClientForHost("acme.localhost");using var host=factory.CreateClientForHost("localhost");var tenant=await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(client,token);
        client.DefaultRequestHeaders.Authorization=new("Bearer",await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host,await factory.CreateHostAccessTokenAsync([WebhookPermissions.SubscriptionsRead,WebhookPermissions.SubscriptionsManage,WebhookPermissions.DeliveriesRead,"tenancy.tenants.switch"],token),tenant.Id,token));
        using var created=await client.PostAsJsonAsync("/api/v1/webhooks/subscriptions",new CreateWebhookSubscriptionRequest("fullnet.workflow.instance.completed","https://93.184.216.34/signed",Receiver.Secret),token);Assert.AreEqual(HttpStatusCode.OK,created.StatusCode);
        var subscription=(await created.Content.ReadFromJsonAsync<WebhookSubscriptionResponse>(token))!;Assert.AreNotEqual(Guid.Empty,subscription.SigningKeyId);
        var eventId=Guid.CreateVersion7();var envelope=new WebhookEventEnvelope(subscription.EventType,eventId,tenant.Id,DateTimeOffset.UtcNow.AddDays(-90),new(Guid.CreateVersion7(),Guid.CreateVersion7(),"test","old-business-event"));
        await using(var scope=factory.Services.CreateAsyncScope())
        {scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();await scope.ServiceProvider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async cancellation=>{await scope.ServiceProvider.GetRequiredService<WebhookEventEnqueueService>().EnqueueAsync(tenant.Id,envelope.EventType,eventId,envelope,cancellation);return 0;},token);}
        clock.Freeze();Assert.AreEqual(1,await ProcessAsync());Assert.AreEqual(1,receiver.Items.Count);
        var first=receiver.Items[0];Assert.AreEqual("1",first.Version);Assert.AreEqual(subscription.SigningKeyId,first.Stamp.KeyId);Assert.AreEqual(eventId,first.Stamp.EventId);Assert.AreEqual(clock.UtcNow.ToUnixTimeSeconds(),first.Stamp.Timestamp);
        Assert.AreEqual(WebhookSignatureHelper.ComputeSignature(Receiver.Secret,first.Body,first.Stamp),first.Signature);
        clock.Offset=TimeSpan.FromSeconds(3);Assert.AreEqual(1,await ProcessAsync());Assert.AreEqual(2,receiver.Items.Count);
        var second=receiver.Items[1];Assert.AreEqual(first.Stamp.Timestamp+3,second.Stamp.Timestamp);Assert.AreEqual(first.Stamp.DeliveryId,second.Stamp.DeliveryId);Assert.AreEqual(first.Stamp.EventId,second.Stamp.EventId);Assert.AreEqual(first.Stamp.KeyId,second.Stamp.KeyId);Assert.AreEqual(first.Body,second.Body);Assert.AreNotEqual(first.Signature,second.Signature);
        Assert.AreEqual(WebhookSignatureHelper.ComputeSignature(Receiver.Secret,second.Body,second.Stamp),second.Signature);
        var page=await client.GetFromJsonAsync<PagedResult<WebhookDeliveryHistoryResponse>>($"/api/v1/webhooks/subscriptions/{subscription.Id:D}/deliveries",token);Assert.AreEqual("delivered",page!.Items.Single().StatusKey);Assert.AreEqual(2,page.Items.Single().AttemptCount);
        Assert.AreEqual(0,await ProcessAsync());
        async Task<int> ProcessAsync(){await using var scope=factory.Services.CreateAsyncScope();scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();return await scope.ServiceProvider.GetRequiredService<WebhookDeliveryBatchProcessor>().ProcessPendingAsync(token);}
    }
    private sealed class Clock:IClock
    {private DateTimeOffset? frozen;internal TimeSpan Offset;internal void Freeze(){var now=DateTimeOffset.UtcNow;frozen=new DateTimeOffset(now.Ticks-now.Ticks%10,TimeSpan.Zero);}public DateTimeOffset UtcNow=>(frozen??DateTimeOffset.UtcNow)+Offset;}
    private sealed record Sent(string Version,string Signature,string Body,WebhookSignatureStamp Stamp);
    private sealed class Receiver:IHttpClientFactory
    {
        internal const string Secret="independent-signed-delivery-secret";internal readonly List<Sent> Items=[];
        public HttpClient CreateClient(string name)=>new(new Handler(async(request,token)=>{
            var stamp=new WebhookSignatureStamp(Guid.Parse(request.Headers.GetValues("X-FullNet-Key-Id").Single()),Guid.Parse(request.Headers.GetValues("X-FullNet-Event-Id").Single()),Guid.Parse(request.Headers.GetValues("X-FullNet-Delivery-Id").Single()),request.Headers.GetValues("X-FullNet-Event-Type").Single(),long.Parse(request.Headers.GetValues("X-FullNet-Timestamp").Single(),System.Globalization.CultureInfo.InvariantCulture));
            Items.Add(new(request.Headers.GetValues("X-FullNet-Signature-Version").Single(),request.Headers.GetValues("X-FullNet-Signature").Single(),await request.Content!.ReadAsStringAsync(token),stamp));return new HttpResponseMessage(Items.Count==1?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK);
        }));
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
}
