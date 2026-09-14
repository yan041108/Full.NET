using System.Net;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>校验 ImportExport 端点的 OpenAPI 路径、响应与核心 schema。</summary>
internal static class OpenApiImportExportContractAssertions
{
    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        AssertPilotOperations(document.RootElement);
        var paths = document.RootElement.GetProperty("paths");
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/schemas"),
            "get",
            ["200", "401", "403"]);
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/tasks"),
            "post",
            ["201", "400", "401", "403", "404", "422"]);
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/tasks"),
            "get",
            ["200", "401", "403"]);
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/tasks/{taskId}"),
            "get",
            ["200", "401", "403", "404"]);
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/tasks/{taskId}/execute"),
            "post",
            ["200", "401", "403", "404", "422"]);
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/tasks/{taskId}/resume"),
            "post",
            ["200", "401", "403", "404", "422"]);
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/tasks/{taskId}/retry"),
            "post",
            ["200", "401", "403", "404", "422"]);
        AssertOperation(
            paths.GetProperty("/api/v1/import-export/tasks/{taskId}/error-receipt"),
            "get",
            ["200", "401", "403", "404", "422"]);

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        AssertSchema(
            schemas,
            "ImportExportTaskDetailResponse",
            [
                "id",
                "tenantId",
                "schemaKey",
                "worksheetKey",
                "sourceFileId",
                "statusKey",
                "totalRows",
                "validRowCount",
                "invalidRowCount",
                "processedRowCount",
                "succeededRowCount",
                "executionFailedRowCount",
                "nextLineNumber",
                "hasErrorReceipt",
                "previewRows",
                "version",
            ]);
    }

    private static void AssertPilotOperations(JsonElement document)
    {
        const string schemaTag = "ImportExportStaticSchemas";
        const string taskTag = "ImportExportTasks";
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/schemas",
            HttpMethod.Get,
            "importExportListStaticSchemas",
            schemaTag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/tasks",
            HttpMethod.Post,
            "importExportCreateImportTask",
            taskTag,
            201,
            "application/json",
            "multipart/form-data");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/tasks",
            HttpMethod.Get,
            "importExportListImportTasks",
            taskTag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/tasks/{taskId}",
            HttpMethod.Get,
            "importExportGetImportTask",
            taskTag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/tasks/{taskId}/execute",
            HttpMethod.Post,
            "importExportExecuteImportTask",
            taskTag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/tasks/{taskId}/resume",
            HttpMethod.Post,
            "importExportResumeImportTask",
            taskTag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/tasks/{taskId}/retry",
            HttpMethod.Post,
            "importExportRetryImportTask",
            taskTag,
            200,
            "application/json");
        OpenApiPilotContractAssertions.AssertOperation(
            document,
            "/api/v1/import-export/tasks/{taskId}/error-receipt",
            HttpMethod.Get,
            "importExportDownloadImportTaskErrorReceipt",
            taskTag,
            200,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    private static void AssertOperation(
        JsonElement path,
        string method,
        IReadOnlyList<string> statuses)
    {
        var responses = path.GetProperty(method).GetProperty("responses");
        foreach (var status in statuses)
        {
            Assert.IsTrue(
                responses.TryGetProperty(status, out _),
                $"{method.ToUpperInvariant()} 缺少响应状态 {status}");
        }
    }

    private static void AssertSchema(
        JsonElement schemas,
        string suffix,
        IReadOnlyList<string> properties)
    {
        var schema = schemas.EnumerateObject()
            .Single(candidate =>
                string.Equals(candidate.Name, suffix, StringComparison.Ordinal)
                || candidate.Name.EndsWith("." + suffix, StringComparison.Ordinal))
            .Value;
        var actual = schema.GetProperty("properties");
        foreach (var property in properties)
        {
            Assert.IsTrue(
                actual.TryGetProperty(property, out _),
                $"{suffix} 缺少属性 {property}");
        }
    }
}
