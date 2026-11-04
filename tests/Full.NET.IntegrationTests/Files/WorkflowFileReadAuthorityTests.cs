using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Files;

/// <summary>真实双库元数据和企业状态跨存储等待重新校验；存储提供程序只注入可控等待窗口。</summary>
[TestClass]
public sealed class WorkflowFileReadAuthorityTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Authority_changes_during_storage_open_are_rejected_and_release_the_stream(DatabaseProvider provider)
    {
        var connection=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var clock=new FixtureClock();var storage=new PausingProvider();
        using var factory=new FullNetApiFactory(provider,connection,configureTestServices:services=>{
            services.AddSingleton<IClock>(clock);services.AddSingleton<IFileStorageProvider>(storage);
        });
        await factory.InitializeAsync();using var client=factory.CreateClientForHost("localhost");
        var token=await IntegrationTestAuthHelper.LoginAsHostUserAsync(client,"admin");client.DefaultRequestHeaders.Authorization=new("Bearer",token);
        var available=(await client.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!;
        var issuer=available.Single(item=>item.Identifier=="acme").Id;
        var code="read-"+Guid.CreateVersion7().ToString("N");
        using var created=await client.PostAsJsonAsync("/api/v1/tenancy/tenants",new ProvisionTenantRequest(code,"File read recipient",code+".localhost"));
        Assert.AreEqual(HttpStatusCode.Created,created.StatusCode,await created.Content.ReadAsStringAsync());
        var recipient=(await created.Content.ReadFromJsonAsync<TenantSummary>())!.Id;
        await using DbConnection db=provider==DatabaseProvider.SqlServer?new SqlConnection(connection):new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await db.OpenAsync();await using var scope=factory.Services.CreateAsyncScope();
        var writer=scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();var current=scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        var commands=scope.ServiceProvider.GetRequiredService<ICommandExecutor>();var store=scope.ServiceProvider.GetRequiredService<IWorkflowFileReadCapabilityStore>();
        var bytes=new byte[]{1,2,3};var hash=Convert.ToHexStringLower(SHA256.HashData(bytes));var initial=clock.UtcNow;
        foreach(var scenario in new[]{"ok","revoked","expired","issuer_inactive","recipient_inactive","source_released"})
        {
            clock.UtcNow=initial;var fileId=Guid.CreateVersion7();var resourceId=Guid.CreateVersion7();var callId=Guid.CreateVersion7();var storageKey=fileId.ToString("N");
            writer.SetTenant(new TenantContext(issuer,"acme","Issuer"));
            // 固定本模块的源文件元数据，不将此夹具冒充上传或工作流签发旅程。
            var parameters=new Dictionary<string,object?>{["Id"]=fileId,["TenantId"]=issuer,["OwnerModuleKey"]="workflow",["ResourceId"]=resourceId,
                ["OriginalFileName"]="source.bin",["ContentType"]="application/octet-stream",["SizeBytes"]=bytes.Length,["ContentHash"]=hash,
                ["ProviderKey"]=storage.ProviderKey,["StorageKey"]=storageKey,["CreatedByUserId"]=Guid.CreateVersion7(),["CreatedAtUtc"]=initial};
            Assert.AreEqual(1,await commands.ExecuteAsync(TenantResourceFileSql.Insert,parameters));
            Assert.AreEqual(1,await commands.ExecuteAsync(TenantResourceFileSql.MarkReady,parameters));
            var issued=await store.IssueAsync(new(callId,"input","files",fileId,resourceId,recipient,initial.AddMinutes(5)));
            Assert.IsTrue(issued.IsSuccess,issued.Error?.Code);var capability=issued.Value!;
            writer.SetTenant(new TenantContext(recipient,code,"Recipient"));
            storage.BeforeReturn=async()=>{
                if(scenario=="revoked")Assert.AreEqual(1,await db.ExecuteAsync("UPDATE fn_files_read_capability SET StatusKey='revoked', RevokedAtUtc=CreatedAtUtc, Revision=Revision+1 WHERE Id=@Id",new{Id=capability.Id}));
                if(scenario=="expired")clock.UtcNow=capability.ExpiresAtUtc;
                if(scenario is "issuer_inactive" or "recipient_inactive")Assert.AreEqual(1,await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=@Active WHERE Id=@Id",new{Active=false,Id=scenario=="issuer_inactive"?issuer:recipient}));
                if(scenario=="source_released")Assert.AreEqual(1,await db.ExecuteAsync("UPDATE fn_files_tenant_resource_file SET StatusKey='released' WHERE Id=@Id",new{Id=fileId}));
            };
            var before=storage.OpenCount;var opened=await store.OpenAsync(capability.Id,callId);
            Assert.AreEqual(before+1,storage.OpenCount);Assert.AreEqual(storageKey,storage.LastKey);Assert.AreEqual(recipient,current.Id);
            if(scenario=="ok")
            {
                Assert.IsTrue(opened.IsSuccess,opened.Error?.Code);Assert.IsFalse(storage.LastStream!.Disposed);
                using var copy=new MemoryStream();await opened.Value!.Content.CopyToAsync(copy);CollectionAssert.AreEqual(bytes,copy.ToArray());
                await opened.Value.Content.DisposeAsync();
            }
            else {Assert.IsFalse(opened.IsSuccess,scenario);Assert.AreEqual(FilesErrorCodes.ReadCapabilityNotFound,opened.Error?.Code);}
            Assert.IsTrue(storage.LastStream!.Disposed,scenario);
            if(scenario is "issuer_inactive" or "recipient_inactive")await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET IsActive=@Active WHERE Id=@Id",new{Active=true,Id=scenario=="issuer_inactive"?issuer:recipient});
        }
        Assert.AreEqual(0,storage.DeleteCount);
    }

    private sealed class FixtureClock:IClock {public DateTimeOffset UtcNow {get;set;}=DateTimeOffset.UtcNow;}
    private sealed class TrackedStream():MemoryStream([1,2,3])
    {
        public bool Disposed {get;private set;}
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
    }
    private sealed class PausingProvider:IFileStorageProvider
    {
        public string ProviderKey=>"read_authority_fixture";
        public Func<Task> BeforeReturn {get;set;}=()=>Task.CompletedTask;
        public int OpenCount {get;private set;}
        public int DeleteCount {get;private set;}
        public string? LastKey {get;private set;}
        public TrackedStream? LastStream {get;private set;}
        public async Task<Stream> OpenReadAsync(string storageKey,CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();OpenCount++;LastKey=storageKey;LastStream=new();
            try{await BeforeReturn();return LastStream;}catch{await LastStream.DisposeAsync();throw;}
        }
        public Task SaveAsync(string storageKey,Stream content,CancellationToken cancellationToken)=>throw new NotSupportedException();
        public Task DeleteAsync(string storageKey,CancellationToken cancellationToken){DeleteCount++;throw new NotSupportedException();}
    }
}
