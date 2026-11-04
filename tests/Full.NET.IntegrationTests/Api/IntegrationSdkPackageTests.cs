using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Webhooks.Contracts;
using Full.NET.Modules.Webhooks.Delivery;
using Full.NET.Modules.Webhooks.Features.DeliverWebhooks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>独立安装后的 SDK 通过真实 Kestrel 调用生产 API；打包入口单独准入，不进入通用数据库分片。</summary>
[TestClass]
[TestCategory("IntegrationSdkPackage")]
public sealed partial class IntegrationSdkPackageTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Installed_sdk_authorizes_consumes_production_event_and_observes_tenant_isolation_and_revocation(DatabaseProvider provider)
    {
        var directory = Environment.GetEnvironmentVariable("FULLNET_SDK_CONSUMER_DIRECTORY");
        Assert.IsTrue(!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, "consumer.mjs")), "Run pnpm test:integration-sdk:package to prepare the isolated installed consumer.");
        var consumerRoot=directory!;directory=Path.Combine(consumerRoot,provider.ToString());Directory.CreateDirectory(directory);
        foreach(var file in new[]{"consumer.mjs","receiver.mjs"})File.Copy(Path.Combine(consumerRoot,file),Path.Combine(directory,file));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)); var token = timeout.Token;
        var cs=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, cs, new Dictionary<string,string?> { ["Tenancy:HostDomains:1"]="127.0.0.1",["DataProtection:KeyRingPath"]=Path.Combine(directory,"keys") },includeEnterpriseRequest:true);
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback,0)); await factory.InitializeAsync(token);
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var host = new HttpClient { BaseAddress=new Uri(address) }; host.DefaultRequestHeaders.Host="localhost";
        using var a = new HttpClient { BaseAddress=new Uri(address) }; a.DefaultRequestHeaders.Host="acme.localhost";
        var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(a,token); TenantSummary b;
        var domain="sdk-b-"+Guid.CreateVersion7().ToString("N")+".localhost";
        await using(var scope=factory.Services.CreateAsyncScope())
        { scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();var result=await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(new("sdk-b-"+Guid.CreateVersion7().ToString("N"),"SDK B",domain),token);Assert.IsTrue(result.IsSuccess);b=result.Value!; }
        string[] rights=[WebhookPermissions.SubscriptionsRead,WebhookPermissions.SubscriptionsManage,WebhookPermissions.SubscriptionsSetStatus,WebhookPermissions.DeliveriesRead,WebhookPermissions.SigningKeysRotate,"tenancy.tenants.switch"];
        var identity=await factory.CreateHostIdentityAsync("sdk-a-"+Guid.CreateVersion7().ToString("N"),rights,token);
        var accessA=await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host,identity.AccessToken,tenant.Id,token);
        var accessB=await IntegrationTestTenantContextHelper.SwitchToTenantAsync(host,await factory.CreateHostAccessTokenAsync(rights,token),b.Id,token);
        const string secret="sdk-independent-signing-secret";
        const string rotatedSecret="sdk-rotated-independent-secret";
        var environment=new Dictionary<string,string>{{"SDK_API_URL",address},{"SDK_TENANT_A",tenant.Id.ToString("D")},{"SDK_TENANT_B",b.Id.ToString("D")},{"SDK_TOKEN_A",accessA},{"SDK_TOKEN_B",accessB},{"SDK_HOST_B",domain},{"SDK_SIGNING_SECRET",secret},{"SDK_ROTATED_SIGNING_SECRET",rotatedSecret}};
        await RunAsync("bootstrap");
        using var bootstrap=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory!,"bootstrap.json"),token));var subscriptionId=bootstrap.RootElement.GetProperty("subscriptionId").GetGuid();
        var signingKeyId=bootstrap.RootElement.GetProperty("signingKeyId").GetGuid(); environment["SDK_KEY_ID"]=signingKeyId.ToString("D");
        environment["SDK_SIGNING_SECRET"]=rotatedSecret;
        await RunBusinessConsumerAsync(factory,host,subscriptionId,tenant.Id,signingKeyId,rotatedSecret,directory!,environment,RunAsync,token);
        host.DefaultRequestHeaders.Authorization=new("Bearer",await factory.CreateHostAccessTokenAsync([IdentitySessionManagementPermissions.Revoke],token));
        using var revoked=await host.PostAsync($"/api/v1/identity/online-sessions/users/{identity.UserId:D}/revoke-all",null,token);Assert.AreEqual(HttpStatusCode.OK,revoked.StatusCode);
        await RunAsync("revoked");
        async Task RunAsync(string phase)
        {
            var start=new ProcessStartInfo("node"){WorkingDirectory=directory!,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};start.ArgumentList.Add("consumer.mjs");start.ArgumentList.Add(phase);
            foreach(var item in environment)start.Environment[item.Key]=item.Value;
            using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync(token);var error=process.StandardError.ReadToEndAsync(token);
            try{await process.WaitForExitAsync(token);Assert.AreEqual(0,process.ExitCode,await output+await error);}
            finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}}
        }
    }
}
