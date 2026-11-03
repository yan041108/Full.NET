using System.Net;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>FAQ 数据库修订、真实完整代次与双库发布；独立的 Schema 导出不计为测试通过。</summary>
[TestClass] [DoNotParallelize]
public sealed class AiKnowledgeFaqLifecycleApiTests
{
    [TestMethod] [DataRow(DatabaseProvider.SqlServer)] [DataRow(DatabaseProvider.MySql)]
    public Task Faq_revision_index_review_publish_and_revoke_preserve_provenance(DatabaseProvider provider) =>
        NativeHumanKnowledgePublicationE2ETests.VerifyAsync(provider, false, verifyFaqLifecycle: true);

    /// <summary>临时 CLI 的真实运行时契约输入；只导出 Schema，不跳过场景后登记绿色结果。</summary>
    public static async Task ExportOpenApiAsync(string output, bool mysql)
    {
        var provider = mysql ? DatabaseProvider.MySql : DatabaseProvider.SqlServer;
        var connection = mysql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        if (Environment.GetEnvironmentVariable("FULLNET_K04_LIFECYCLE_DATABASE_REGISTRY") is { Length: > 0 } registry)
        {
            await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(!mysql, connection);
            await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = db.Database, purpose = "openapi" }) + "\n");
        }
        using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync(); using var client = factory.CreateClientForHost("localhost");
        using var response = await client.GetAsync("/openapi/v1.json");
        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("Runtime OpenAPI export failed.");
        await File.WriteAllTextAsync(output, await response.Content.ReadAsStringAsync());
    }
}

/// <summary>仅本批夹具定位 SQL/行类型错误；不替换正式错误映射，不写入生产日志或公开响应。</summary>
internal sealed class FaqLifecycleDiagnostic : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(Microsoft.AspNetCore.Http.HttpContext context, Exception exception, CancellationToken token)
    {
        if(exception is InvalidCastException or System.Data.Common.DbException)
            Console.WriteLine("FAQ lifecycle fixture diagnostic: "+exception.GetType().Name+" "+exception.Message+"\n"+exception.StackTrace);
        return ValueTask.FromResult(false);
    }
}
