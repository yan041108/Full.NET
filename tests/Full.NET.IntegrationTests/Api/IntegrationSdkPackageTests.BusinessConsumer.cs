using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Api;

public sealed partial class IntegrationSdkPackageTests
{
    /// <summary>业务 HTTP 审批→正式事务 Outbox→独立 Worker→真实 Node HTTP 接收；不直接调用事件 Handler 或合成完成载荷。</summary>
    private static async Task RunBusinessConsumerAsync(FullNetApiFactory factory,HttpClient business,Guid subscriptionId,Guid tenantId,Guid keyId,string secret,
        string directory,Dictionary<string,string> environment,Func<string,Task> runConsumer,CancellationToken token)
    {
        var access=await EnterpriseRequestAssertions.LoginAndEnterAcmeTenantAsync(business,token);
        var organization=await EnterpriseRequestAssertions.CreateOrganizationUnitAsync(business,access,token);
        await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(business,access,token);
        var actor=await EnterpriseRequestAssertions.GetCurrentUserIdAsync(business,access,token);
        business.DefaultRequestHeaders.Authorization=new("Bearer",access);
        using var create=new HttpRequestMessage(HttpMethod.Post,"/api/v1/enterprise_request/enterprise-requests")
        {Content=JsonContent.Create(new CreateEnterpriseRequestRequest("SDK-"+Guid.CreateVersion7().ToString("N")[..12],"Real webhook business approval",EnterpriseRequestStatusKeys.Draft,42m,actor))};
        create.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId,organization.ToString("D"));
        using var created=await business.SendAsync(create,token);Assert.AreEqual(HttpStatusCode.Created,created.StatusCode,await created.Content.ReadAsStringAsync(token));
        var request=(await created.Content.ReadFromJsonAsync<EnterpriseRequestResponse>(token))!;
        await using var receiver=await ReceiverProcess.StartAsync(directory,tenantId,keyId,secret,token);
        var retryMarker=Path.Combine(directory,"webhook-after-enqueue");
        await using var worker=await EnterpriseRequestWorkerProcess.StartAsync(factory,directory,"none",retryMarker,0,receiver.Url,webhookFailAfterEnqueueOnce:true);
        var root=$"/api/v1/enterprise_request/enterprise-requests/{request.Id:D}";
        using var submitted=await business.PostAsync(root+"/submit-for-approval",null,token);Assert.AreEqual(HttpStatusCode.OK,submitted.StatusCode,await submitted.Content.ReadAsStringAsync(token));
        using var progress=JsonDocument.Parse(await business.GetStringAsync(root+"/approval-progress",token));var instance=progress.RootElement.GetProperty("workflowInstanceId").GetGuid();
        Guid todo=Guid.Empty;long revision=0;
        await WaitAsync(async()=>
        {
            using var todos=JsonDocument.Parse(await business.GetStringAsync("/api/v1/workflow/todos/mine?page=1&pageSize=100",token));
            foreach(var item in todos.RootElement.GetProperty("items").EnumerateArray())if(item.GetProperty("instanceId").GetGuid()==instance){todo=item.GetProperty("id").GetGuid();revision=item.GetProperty("revision").GetInt64();return true;}
            return false;
        },"The real business workflow did not produce its assigned todo.",token);
        using var runtime=JsonDocument.Parse(await business.GetStringAsync($"/api/v1/workflow/todos/{todo:D}/runtime",token));
        var formRevision=runtime.RootElement.GetProperty("submissionRevision").GetInt64();
        var approval=new{expectedRevision=revision,expectedFormRevision=formRevision,fieldPatch=new Dictionary<string,object?>(),comment="Actual approved business",idempotencyKey="sdk-business-approval"};
        using(var approved=await business.PostAsJsonAsync($"/api/v1/workflow/todos/{todo:D}/approve",approval,token))Assert.AreEqual(HttpStatusCode.OK,approved.StatusCode,await approved.Content.ReadAsStringAsync(token));
        // 同一办理命令复放不能生成第二个完成事件、Webhook 投递或消费结果。
        using(var repeated=await business.PostAsJsonAsync($"/api/v1/workflow/todos/{todo:D}/approve",approval,token))Assert.AreEqual(HttpStatusCode.OK,repeated.StatusCode,await repeated.Content.ReadAsStringAsync(token));
        await using DbConnection db=factory.Provider==DatabaseProvider.SqlServer?new SqlConnection(factory.ConnectionString):ReviewFixMigrationRecoverySupport.MySqlConnection(factory.ConnectionString);await db.OpenAsync(token);
        await WaitAsync(async()=>
        {
            var current=await business.GetFromJsonAsync<EnterpriseRequestResponse>(root,token);
            return current?.Status==EnterpriseRequestStatusKeys.Approved&&File.Exists(Path.Combine(directory,"event-input.json"))
                &&await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery WHERE SubscriptionId=@Id AND Status='Delivered'",new{Id=subscriptionId})==1;
        },"The formal Worker did not finish the business outcome and real webhook delivery. Check this test's worker logs.",token);
        using var frame=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory,"event-input.json"),token));var eventId=frame.RootElement.GetProperty("eventId").GetGuid();
        Assert.IsTrue(File.Exists(retryMarker));Assert.AreEqual(eventId.ToString("D"),await File.ReadAllTextAsync(retryMarker,token));
        await WaitAsync(async()=>await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id=@Id AND ProcessedAtUtc IS NOT NULL",new{Id=eventId})==1,"The real Outbox did not recover after the injected enqueue acknowledgement failure.",token);
        Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_outbox_message WHERE Id=@Id AND TenantId=@TenantId AND MessageType=@Type",new{Id=eventId,TenantId=tenantId,Type=WorkflowNotificationIntegrationEventTypes.InstanceCompleted}));
        Assert.AreEqual(1L,await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_webhooks_delivery WHERE SubscriptionId=@Id AND EventId=@EventId",new{Id=subscriptionId,EventId=eventId}));
        environment["SDK_SUBSCRIPTION_ID"]=subscriptionId.ToString("D");environment["SDK_WORKFLOW_INSTANCE_ID"]=instance.ToString("D");environment["SDK_BUSINESS_ID"]=request.Id.ToString("D");environment["SDK_RECEIVER_URL"]=receiver.Url;
        await runConsumer("event");
    }

    private static async Task WaitAsync(Func<Task<bool>> check,string failure,CancellationToken token)
    {
        var elapsed=Stopwatch.StartNew();while(elapsed.Elapsed<TimeSpan.FromSeconds(75)){if(await check())return;await Task.Delay(100,token);}Assert.Fail(failure);
    }

    /// <summary>拥有本测试的 Node 子进程；stdin 正常关闭使持久回执释放锁，超时只终止自己的进程树。</summary>
    private sealed class ReceiverProcess:IAsyncDisposable
    {
        private readonly Process process;private readonly Task<string> output,errors;
        internal string Url{get;}
        private ReceiverProcess(Process process,string url,Task<string> output,Task<string> errors){this.process=process;Url=url;this.output=output;this.errors=errors;}
        internal static async Task<ReceiverProcess> StartAsync(string directory,Guid tenant,Guid key,string secret,CancellationToken token)
        {
            var file=Path.Combine(directory,"receiver-secret");await File.WriteAllTextAsync(file,secret,token);
            var start=new ProcessStartInfo("node"){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};start.ArgumentList.Add("receiver.mjs");
            start.Environment["FULLNET_WEBHOOK_SIGNING_SECRET_FILE"]=file;start.Environment["FULLNET_WEBHOOK_KEY_ID"]=key.ToString("D");start.Environment["FULLNET_WEBHOOK_TENANT_ID"]=tenant.ToString("D");
            var child=Process.Start(start)!;var errors=child.StandardError.ReadToEndAsync(token);
            try{var ready=await child.StandardOutput.ReadLineAsync(token);if(ready is null)Assert.Fail("Receiver exited before readiness: "+await errors);Assert.IsNotNull(ready);Assert.IsTrue(ready.StartsWith("RECEIVER_READY port=",StringComparison.Ordinal),ready);
                Assert.IsTrue(int.TryParse(ready["RECEIVER_READY port=".Length..],out var port)&&port is >0 and <=65535);return new(child,$"http://127.0.0.1:{port}/webhooks",child.StandardOutput.ReadToEndAsync(token),errors);}
            catch{if(!child.HasExited){child.Kill(entireProcessTree:true);await child.WaitForExitAsync(CancellationToken.None);}child.Dispose();throw;}
        }
        public async ValueTask DisposeAsync()
        {
            try{if(!process.HasExited){await process.StandardInput.WriteLineAsync("close");process.StandardInput.Close();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(entireProcessTree:true);await process.WaitForExitAsync(CancellationToken.None);}}
                Assert.AreEqual(0,process.ExitCode,await output+await errors);}
            finally{process.Dispose();}
        }
    }
}
