using System.Net;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>只在隔离夹具中改写来源依赖状态；真实 HTTP 必须拒读并在恢复后读取原始正文。</summary>
internal static class AiKnowledgeSourcePersistenceAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, HttpClient client, Guid knowledgeBaseId,
        Guid versionId, string sourcePath, string content, Guid? tenantId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<ICurrentTenantContextWriter>();
        if (tenantId is { } id) context.SetTenant(new TenantContext(id, "acme", "Acme"));
        else context.SetHost();
        var query = services.GetRequiredService<IQueryExecutor>();
        var commands = services.GetRequiredService<ICommandExecutor>();
        var dataScope = tenantId.HasValue ? SqlDataScope.TenantRequired : SqlDataScope.HostOnly;
        var binding = tenantId.HasValue ? SqlTenantBinding.CurrentTenantId : SqlTenantBinding.None;
        var scopeFilter = tenantId.HasValue ? "knowledge.TenantId=@TenantId" : "knowledge.TenantId IS NULL";
        var parameters = new Dictionary<string, object?> { ["KnowledgeBaseId"] = knowledgeBaseId, ["VersionId"] = versionId };
        var fileId = await query.QuerySingleOrDefaultAsync<Guid>(new SqlStatement("test.ai.source_file_id", """
            SELECT version.FileId FROM fn_ai_knowledge_version AS version
            INNER JOIN fn_ai_knowledge_document AS document ON document.Id=version.DocumentId
            INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId
            WHERE version.Id=@VersionId AND knowledge.Id=@KnowledgeBaseId AND
            """ + " " + scopeFilter, dataScope, binding), parameters);
        Assert.AreNotEqual(Guid.Empty, fileId);
        parameters["FileId"] = fileId;
        var fileFilter = tenantId.HasValue ? "TenantId=@TenantId" : "TenantId IS NULL";
        var table = tenantId.HasValue ? "fn_files_tenant_resource_file" : "fn_files_file";
        var storageKey = await query.QuerySingleOrDefaultAsync<string>(new SqlStatement("test.ai.source_storage_key",
            $"SELECT StorageKey FROM {table} WHERE Id=@FileId AND {fileFilter}", dataScope, binding), parameters);
        var providerKey = await query.QuerySingleOrDefaultAsync<string>(new SqlStatement("test.ai.source_provider_key",
            $"SELECT ProviderKey FROM {table} WHERE Id=@FileId AND {fileFilter}", dataScope, binding), parameters);
        Assert.IsNotNull(storageKey);
        Assert.AreEqual("local", providerKey);
        var root = Path.GetFullPath(services.GetRequiredService<IOptions<LocalFileStorageOptions>>().Value.RootPath);
        var fixtureRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "fullnet-files-integration")) + Path.DirectorySeparatorChar;
        Assert.IsTrue(root.StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase), "对象故障只允许操作本测试工厂的临时文件目录。");

        async Task ExpectDeniedAsync(HttpStatusCode status, string code)
        {
            using var response = await client.GetAsync(sourcePath);
            var body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(status, response.StatusCode, body);
            Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            using var problem = JsonDocument.Parse(body);
            Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
            Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
            var exposed = string.Join('\n', ReadProblemStrings(problem.RootElement).Append(body));
            foreach (var secret in new[] { content, storageKey, root, "FileNotFoundException", "Stored blob was not found", "stackTrace" })
                Assert.IsFalse(exposed.Contains(secret, StringComparison.Ordinal), "拒读响应不能包含正文、对象路径或内部异常信息。");
        }
        async Task ExpectReadableAsync()
        {
            using var response = await client.GetAsync(sourcePath);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual(content, await response.Content.ReadAsStringAsync());
        }
        async Task SetVersionFileAsync(Guid value)
        {
            var changed = new Dictionary<string, object?>(parameters) { ["FileId"] = value };
            Assert.AreEqual(1, await commands.ExecuteAsync(new SqlStatement("test.ai.source_replace_file", """
                UPDATE fn_ai_knowledge_version SET FileId=@FileId
                WHERE Id=@VersionId AND DocumentId IN (
                    SELECT document.Id FROM fn_ai_knowledge_document AS document
                    INNER JOIN fn_ai_knowledge_base AS knowledge ON knowledge.Id=document.KnowledgeBaseId
                    WHERE knowledge.Id=@KnowledgeBaseId AND
                """ + " " + scopeFilter + ")", dataScope, binding), changed));
        }
        async Task SetFileStateAsync(string sql, object? value)
        {
            var changed = new Dictionary<string, object?>(parameters) { ["ChangedValue"] = value };
            Assert.AreEqual(1, await commands.ExecuteAsync(new SqlStatement("test.ai.source_dependency_state", sql, dataScope, binding), changed));
        }

        await ExpectReadableAsync();
        // 模拟 AI 持有不存在的文件引用，不创建任何伪造的 Files 元数据或外部对象。
        try
        {
            await SetVersionFileAsync(Guid.CreateVersion7());
            await ExpectDeniedAsync(HttpStatusCode.NotFound, AiKnowledgeErrorCodes.SourceUnavailable);
        }
        finally { await SetVersionFileAsync(fileId); }
        await ExpectReadableAsync();

        if (tenantId.HasValue)
        {
            // 即使调用者拥有来源权限，同一租户下的其他资源或模块也不能供该版本借用。
            const string resourceSql = "UPDATE fn_files_tenant_resource_file SET ResourceId=@ChangedValue WHERE Id=@FileId AND TenantId=@TenantId";
            try
            {
                await SetFileStateAsync(resourceSql, Guid.CreateVersion7());
                await ExpectDeniedAsync(HttpStatusCode.NotFound, AiKnowledgeErrorCodes.SourceUnavailable);
            }
            finally { await SetFileStateAsync(resourceSql, versionId); }
            await ExpectReadableAsync();
            const string ownerSql = "UPDATE fn_files_tenant_resource_file SET OwnerModuleKey=@ChangedValue WHERE Id=@FileId AND TenantId=@TenantId";
            try
            {
                await SetFileStateAsync(ownerSql, "reporting");
                await ExpectDeniedAsync(HttpStatusCode.NotFound, AiKnowledgeErrorCodes.SourceUnavailable);
            }
            finally { await SetFileStateAsync(ownerSql, "ai"); }
            await ExpectReadableAsync();
            const string releasedSql = "UPDATE fn_files_tenant_resource_file SET StatusKey=@ChangedValue WHERE Id=@FileId AND TenantId=@TenantId";
            try
            {
                await SetFileStateAsync(releasedSql, "released");
                await ExpectDeniedAsync(HttpStatusCode.NotFound, AiKnowledgeErrorCodes.SourceUnavailable);
            }
            finally { await SetFileStateAsync(releasedSql, "ready"); }
        }
        else
        {
            // 模拟存量软删除元数据；正常删除 API 仍由开放 Claim 保护，不在此绕过其授权测试。
            const string deletedSql = "UPDATE fn_files_file SET DeletedAtUtc=@ChangedValue WHERE Id=@FileId AND TenantId IS NULL";
            try
            {
                await SetFileStateAsync(deletedSql, DateTime.UtcNow);
                await ExpectDeniedAsync(HttpStatusCode.NotFound, AiKnowledgeErrorCodes.SourceUnavailable);
            }
            finally { await SetFileStateAsync(deletedSql, null); }
        }
        await ExpectReadableAsync();

        // 只删除夹具专属目录中由该版本引用的对象；finally 按原键恢复相同字节，保留元数据与配额。
        var storage = services.GetRequiredService<FileStorageProviderRegistry>().Resolve(providerKey!);
        try
        {
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            await ExpectDeniedAsync(HttpStatusCode.InternalServerError, CommonErrorCodes.Unexpected);
        }
        finally
        {
            await using var restored = new MemoryStream(Encoding.UTF8.GetBytes(content));
            await storage.SaveAsync(storageKey, restored, CancellationToken.None);
        }
        await ExpectReadableAsync();
    }

    // 先解码 JSON 字符串，避免 Unicode 或反斜杠转义掩盖错误正文中的泄露。
    private static IEnumerable<string> ReadProblemStrings(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().SelectMany(property => ReadProblemStrings(property.Value).Append(property.Name)),
        JsonValueKind.Array => value.EnumerateArray().SelectMany(ReadProblemStrings),
        JsonValueKind.String => [value.GetString()!],
        _ => [],
    };
}
