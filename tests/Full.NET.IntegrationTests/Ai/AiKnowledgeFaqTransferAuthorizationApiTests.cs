using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>精确传输权限不能被页面读取或旧写权限替代。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqTransferAuthorizationApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Faq_transfer_requires_each_operation_and_import_write_permission(DatabaseProvider provider)=>FaqTransferAuthorizationAssertions.VerifyAsync(provider,false);
}

/// <summary>四类授权组合实际请求生产 Native Endpoint，不以客户端隐藏替代权限。</summary>
[TestClass] [DoNotParallelize]
public sealed class NativeKnowledgeFaqTransferAuthorizationE2ETests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Native_faq_transfer_requires_precise_export_and_import_permissions(DatabaseProvider provider)=>FaqTransferAuthorizationAssertions.VerifyAsync(provider,true);
}

internal static class FaqTransferAuthorizationAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider,bool native)
    {
        var artifact=native?NativeApiArtifactLocator.RequireArtifact():null;
        var connection=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings=new Dictionary<string,string?>{
            ["DataProtection:KeyRingPath"]=Path.Combine(Path.GetTempPath(),"fullnet-human-native-keys",Guid.CreateVersion7().ToString("N")),
            ["Files:Local:RootPath"]=Path.Combine(Path.GetTempPath(),"fullnet-human-native-files",Guid.CreateVersion7().ToString("N"))};
        await using(var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,connection))
        {
            if(Environment.GetEnvironmentVariable("FULLNET_K04_TRANSFER_DATABASE_REGISTRY") is {Length:>0} registry)
                await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=provider.ToString(),database=db.Database,native,purpose="faq-transfer-authorization",filesRoot=settings["Files:Local:RootPath"],keysRoot=settings["DataProtection:KeyRingPath"]})+"\n");
        }
        using var factory=new FullNetApiFactory(provider,connection,settingsOverrides:settings);await factory.InitializeAsync();
        await using var api=artifact is null?null:await NativeApiProcessHost.StartAsync(artifact,provider,connection,settings,NativeAotTestTimeouts.ProcessStartup);
        using var client=api?.CreateClient()??factory.CreateClientForHost("localhost");
        var source=Guid.CreateVersion7();var faq=Guid.CreateVersion7();var content=new AiKnowledgeFaqContent("只验证权限",[],"权限场景正文","制度",null,null);
        var package=new AiKnowledgeFaqTransferPackage("faq-transfer-v1",[new(faq,source,Guid.CreateVersion7(),1,"independent","独立依据",new('A',64),new('B',64),content,[])]);
        foreach(var mode in new[]{"write_only","export_only","import_without_write","import_with_write"})
        {
            string[] operation=mode switch{"write_only"=>[AiKnowledgeFaqPermissions.Write],"export_only"=>[AiKnowledgeFaqPermissions.Export],"import_without_write"=>[AiKnowledgeFaqPermissions.Import],_=>[AiKnowledgeFaqPermissions.Import,AiKnowledgeFaqPermissions.Write]};
            var name="faq-transfer-auth-"+Guid.CreateVersion7().ToString("N");
            await factory.CreateHostIdentityAsync(name,[AiKnowledgePermissions.Read,AiKnowledgeFaqPermissions.Read,..operation],password:FullNetApiFactory.TestPassword);
            var token=await IntegrationTestAuthHelper.LoginAsHostUserAsync(client,name);var root=$"/api/v1/ai/knowledge-bases/{source}/faq-transfer";
            await Check(root+"/export",new ExportAiKnowledgeFaqs([faq]),mode=="export_only");
            await Check(root+"/preview",new PreviewAiKnowledgeFaqImport(package),mode=="import_with_write");
            await Check(root+"/import",new ImportAiKnowledgeFaqs(package,new('C',64),[Guid.CreateVersion7()]),mode=="import_with_write",invalidDigest:true);
            async Task Check(string path,object body,bool allowed,bool invalidDigest=false)
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};request.Headers.Authorization=new("Bearer",token);
                using var response=await client.SendAsync(request);var expected=allowed?(invalidDigest?HttpStatusCode.BadRequest:HttpStatusCode.NotFound):HttpStatusCode.Forbidden;
                Assert.AreEqual(expected,response.StatusCode,await response.Content.ReadAsStringAsync());
                if(!allowed){using var error=JsonDocument.Parse(await response.Content.ReadAsStringAsync());Assert.AreEqual("authorization.permission_denied",error.RootElement.GetProperty("code").GetString());}
            }
        }
    }
}
