using System.Net;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只核对本批公开闭合协议与精确金额文本，不重复旧评估集流程或把OpenAPI构造当模型运行。</summary>
[TestClass][DoNotParallelize]
public sealed class AiKnowledgeEvaluationRunContractApiTests
{
    [TestMethod][DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public async Task Run_and_finance_contracts_are_closed_and_preserve_decimal_text(DatabaseProvider provider)
    {
        var connection=provider==DatabaseProvider.SqlServer?await SharedDatabaseFixture.CreateSqlServerDatabaseAsync():await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var stamp=Guid.CreateVersion7().ToString("N");var settings=new Dictionary<string,string?>{["Files:Local:RootPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k05-run-contract-files",stamp),["DataProtection:KeyRingPath"]=Path.Combine(Path.GetTempPath(),"fullnet-k05-run-contract-keys",stamp)};
        await using var db=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,connection);
        if(Environment.GetEnvironmentVariable("FULLNET_K05_EVALUATION_DATABASE_REGISTRY") is {Length:>0} registry)
            await File.AppendAllTextAsync(registry,JsonSerializer.Serialize(new{provider=provider.ToString(),database=db.Database,filesRoot=settings["Files:Local:RootPath"],keysRoot=settings["DataProtection:KeyRingPath"]})+"\n");
        using var factory=new FullNetApiFactory(provider,connection,settingsOverrides:settings);await factory.InitializeAsync();using var client=factory.CreateClientForHost("localhost");
        using var response=await client.GetAsync("/openapi/v1.json");Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);var text=await response.Content.ReadAsStringAsync();using var document=JsonDocument.Parse(text);
        var operations=document.RootElement.GetProperty("paths").EnumerateObject().SelectMany(path=>path.Value.EnumerateObject()).Select(value=>value.Value)
            .Where(value=>value.TryGetProperty("operationId",out var id)&&(id.GetString()!.Contains("KnowledgeEvaluation",StringComparison.Ordinal)||id.GetString() is "aiGetKnowledgeFinance" or "aiGetKnowledgeQualityObservations"))
            .Where(value=>value.GetProperty("tags").EnumerateArray().Any(tag=>tag.GetString()=="AiKnowledgeEvaluationRuns")||value.GetProperty("operationId").GetString() is "aiGetKnowledgeFinance" or "aiGetKnowledgeQualityObservations").ToArray();
        Assert.AreEqual(14,operations.Length);
        var schemas=document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach(var name in new[]{"AiKnowledgeFinanceBucket","AiKnowledgeEvaluationOperation"})
        {var field=schemas.GetProperty(name).GetProperty("properties").GetProperty("recordedCost").GetRawText();StringAssert.Contains(field,"string");Assert.IsFalse(field.Contains("number",StringComparison.Ordinal));}
        var admin=await NativeApiE2EAssertions.LoginAsync(client);
        await KnowledgeEvaluationRunAssertions.VerifyLocalizedErrorsAsync(client,Guid.CreateVersion7(),admin);
        Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        if(Environment.GetEnvironmentVariable("FULLNET_K05_RUN_OPENAPI_EXPORT_DIR") is {Length:>0} export)
        {Directory.CreateDirectory(export);await File.WriteAllTextAsync(Path.Combine(export,"run-"+provider.ToString().ToLowerInvariant()+".json"),text);}
    }
}
