using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>正式页面创建原运行、另一操作者批准、Native Worker投递；后台不代替页面批准或完成。</summary>
internal static class PersonalScheduleCompletionBrowserScenario
{
    internal static async Task VerifyAsync()
    {
        var bridge=Environment.GetEnvironmentVariable("FULLNET_K06_COMPLETION_BROWSER_BRIDGE");
        if(string.IsNullOrWhiteSpace(bridge)||Directory.Exists(bridge))throw new InvalidOperationException("必须提供全新完成动作桥接目录。");
        Directory.CreateDirectory(bridge);
        if(!NativeWorkerArtifactLocator.TryResolve(out var workerArtifact,out var reason))throw new InvalidOperationException(reason);
        var connection=await SharedDatabaseFixture.CreateMySqlDatabaseAsync();var stamp=Guid.CreateVersion7().ToString("N");
        var settings=AiAgentRuntimeTestSettings.ForIntegrationTests();
        settings["Files:Local:RootPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k06-completion-files",stamp);
        settings["DataProtection:KeyRingPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k06-completion-keys",stamp);
        settings["Identity:AllowedOrigins:1"]="http://localhost:34492";settings["Tenancy:HostDomains:1"]="127.0.0.1";
        settings["FullNet:Ai:AgentRuntime:PollMilliseconds"]="1000";
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(false,connection);await db.OpenAsync();
        if(Environment.GetEnvironmentVariable("FULLNET_K06_COMPLETION_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider="MySql",database=db.Database,browser=true,filesRoot=settings["Files:Local:RootPath"],keysRoot=settings["DataProtection:KeyRingPath"]})+"\n");
        using var factory=new FullNetApiFactory(DatabaseProvider.MySql,connection,settings);await factory.InitializeAsync();
        string[] common=["identity.navigation.read","platform.dashboard.read",AiAgentRunPermissions.Read,AiAgentApprovalPermissions.Read];
        string[] ownerRights=[..common,CalendarPermissions.Read,CalendarPermissions.Create,CalendarPermissions.SetStatus,PersonalScheduleCompletionKeys.Permission,
            AiModelPermissions.Read,AiAgentRunPermissions.Create,AiAgentRunPermissions.Resume,AiAgentApprovalPermissions.Request,AiAgentApprovalPermissions.Decide];
        var pairs=new List<(string Owner,string Reviewer)>();
        foreach(var width in new[]{1440,390})
        {
            var owner="completion-page-owner-"+width+"-"+stamp;var reviewer="completion-page-reviewer-"+width+"-"+stamp;
            await factory.CreateHostIdentityAsync(owner,ownerRights,password:FullNetApiFactory.TestPassword);
            await factory.CreateHostIdentityAsync(reviewer,[..common,AiAgentApprovalPermissions.Decide],password:FullNetApiFactory.TestPassword);
            pairs.Add((owner,reviewer));
        }
        await using var api=await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(),DatabaseProvider.MySql,connection,settings,
            NativeAotTestTimeouts.ProcessStartup,listenAddress:new Uri("http://0.0.0.0:64215/"));
        using var client=api.CreateClient();var admin=await NativeApiE2EAssertions.LoginAsync(client,api.LogFilePath);
        using var modelRequest=new HttpRequestMessage(HttpMethod.Post,"/api/v1/ai/model-configs")
        {Content=JsonContent.Create(new CreateAiModelConfigRequest(null,"完成动作配置（无模型派发）","ollama","https://provider.test","metadata",null,null,false,true))};
        modelRequest.Headers.Authorization=new("Bearer",admin);using var modelResponse=await client.SendAsync(modelRequest);
        Assert.AreEqual(HttpStatusCode.Created,modelResponse.StatusCode);var model=await modelResponse.Content.ReadFromJsonAsync<AiModelConfigResponse>();Assert.IsNotNull(model);
        var prepared=new List<object>();
        foreach(var pair in pairs)
        {
            using var ownerClient=api.CreateClient();var token=await IntegrationTestAuthHelper.LoginAsHostUserAsync(ownerClient,pair.Owner);
            var now=DateTimeOffset.UtcNow;
            using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/calendar/my-personal-schedules")
            {Content=JsonContent.Create(new CreatePersonalScheduleRequest("PRIVATE_COMPLETION_PAGE_"+pair.Owner,now,now.AddMinutes(20)))};
            request.Headers.Authorization=new("Bearer",token);using var response=await client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.Created,response.StatusCode);var schedule=await response.Content.ReadFromJsonAsync<PersonalScheduleResponse>();Assert.IsNotNull(schedule);
            prepared.Add(new{username=pair.Owner,reviewer=pair.Reviewer,scheduleId=schedule.Id,version=schedule.Version,content=schedule.Content});
        }
        await using var worker=await NativeWorkerProcessHost.StartAsync(workerArtifact,DatabaseProvider.MySql,connection,NativeAotTestTimeouts.ProcessStartup,additionalSettings:settings);
        await File.WriteAllTextAsync(Path.Combine(bridge,"ready.writing.json"),JsonSerializer.Serialize(new{password=FullNetApiFactory.TestPassword,modelId=model.Id,modelName=model.Name,users=prepared}));
        File.Move(Path.Combine(bridge,"ready.writing.json"),Path.Combine(bridge,"ready.json"));
        foreach(var width in new[]{1440,390})await Wait("complete-"+width+".json");
        Assert.AreEqual(2,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_calendar_schedule_completion WHERE Outcome='completed'"));
        Assert.AreEqual(2,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_schedule_completion_intent WHERE Outcome='completed'"));
        Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
        async Task Wait(string file)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(10));
            while(!File.Exists(Path.Combine(bridge,file)))
            {if(File.Exists(Path.Combine(bridge,"failed.json")))Assert.Fail("真实页面失败，保留原记录。");await Task.Delay(250,timeout.Token);}
            using var value=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(bridge,file),timeout.Token));Assert.IsTrue(value.RootElement.GetProperty("passed").GetBoolean());
        }
    }
}
