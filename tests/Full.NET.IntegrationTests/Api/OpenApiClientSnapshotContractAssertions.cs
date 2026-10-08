using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 从真实 Integration Host 导出并核对客户端 OpenAPI 规范化快照。
/// </summary>
internal static class OpenApiClientSnapshotContractAssertions
{
    private const string ExportPathEnvironmentVariable =
        "FULLNET_CLIENT_OPENAPI_EXPORT_PATH";

    public static async Task VerifyAsync(
        HttpClient client,
        DatabaseProvider provider,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("/openapi/v1.json", cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var rawDocument = await response.Content.ReadAsStringAsync(cancellationToken);
        AssertOmittablePropertiesAreOptional(rawDocument);
        AssertKnowledgeFileContract(rawDocument);

        var exportPath = Environment.GetEnvironmentVariable(ExportPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            var exportDirectory = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrWhiteSpace(exportDirectory))
            {
                Directory.CreateDirectory(exportDirectory);
            }
            await File.WriteAllTextAsync(
                exportPath,
                rawDocument,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-client-openapi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var rawPath = Path.Combine(temporaryDirectory, "runtime.openapi.json");
            var normalizedPath = Path.Combine(temporaryDirectory, "normalized.openapi.json");
            await File.WriteAllTextAsync(
                rawPath,
                rawDocument,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            await NormalizeAsync(
                repositoryRoot,
                rawPath,
                normalizedPath,
                cancellationToken);

            var expectedPath = Path.Combine(
                repositoryRoot,
                "contracts",
                "openapi",
                "fullnet-client-v1.openapi.json");
            var expected = (await File.ReadAllTextAsync(expectedPath, cancellationToken))
                .ReplaceLineEndings("\n");
            var actual = (await File.ReadAllTextAsync(normalizedPath, cancellationToken))
                .ReplaceLineEndings("\n");
            Assert.AreEqual(
                expected,
                actual,
                $"{provider} 客户端 OpenAPI 快照漂移：{FindFirstDifference(expected, actual)}");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    /// <summary>上传必须表达原始二进制流，来源下载和删除不得伪装成 JSON 成功响应。</summary>
    private static void AssertKnowledgeFileContract(string rawDocument)
    {
        using var document = JsonDocument.Parse(rawDocument);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        const string route = "/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}";
        var upload = paths.GetProperty(route + "/versions").GetProperty("post");
        Assert.AreEqual("aiUploadKnowledgeVersion", upload.GetProperty("operationId").GetString());
        var body = upload.GetProperty("requestBody");
        Assert.IsTrue(body.GetProperty("required").GetBoolean());
        var content = body.GetProperty("content");
        CollectionAssert.AreEquivalent(new[] { "application/pdf", "text/markdown", "text/plain",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            content.EnumerateObject().Select(item => item.Name).ToArray());
        foreach (var item in content.EnumerateObject())
        {
            var schema = item.Value.GetProperty("schema");
            if (schema.TryGetProperty("$ref", out var reference))
                schema = root.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]);
            Assert.AreEqual("string", schema.GetProperty("type").GetString());
            Assert.AreEqual("binary", schema.GetProperty("format").GetString());
        }
        Assert.IsTrue(upload.GetProperty("security").GetArrayLength() > 0);
        CollectionAssert.AreEquivalent(new[] { "knowledgeBaseId", "documentId", "version", "requestKey", "fileName" },
            upload.GetProperty("parameters").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray());
        Assert.IsTrue(upload.GetProperty("parameters").EnumerateArray().All(item => item.GetProperty("required").GetBoolean()));
        Assert.IsTrue(upload.GetProperty("responses").GetProperty("202").GetProperty("content").TryGetProperty("application/json", out _));
        var source = paths.GetProperty(route + "/versions/{versionId}/source").GetProperty("get");
        Assert.IsTrue(source.GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("application/octet-stream", out _));
        var deleted = paths.GetProperty(route).GetProperty("delete").GetProperty("responses").GetProperty("204");
        Assert.IsFalse(deleted.TryGetProperty("content", out _));
    }

    /// <summary>
    /// 核对响应序列化时允许省略的兼容字段不会被客户端契约误标为必填。
    /// </summary>
    /// <param name="rawDocument">运行时导出的原始 OpenAPI 文档。</param>
    private static void AssertOmittablePropertiesAreOptional(string rawDocument)
    {
        using var document = JsonDocument.Parse(rawDocument);
        var requestSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("CodeGenerationPreviewRequest");
        var requiredProperties = requestSchema
            .GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString())
            .ToArray();

        Assert.IsFalse(
            requiredProperties.Contains("hasVersion", StringComparer.Ordinal),
            "CodeGenerationPreviewRequest.hasVersion 可在现代响应中省略，不得生成必填客户端守卫。");
        var citation = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("AiKnowledgeCitationResponse");
        Assert.IsTrue(citation.GetProperty("properties").TryGetProperty("docxLocation", out _));
        // TXT/PDF 及旧历史可以省略 DOCX 坐标，客户端不能要求新增兼容字段。
        Assert.IsFalse(citation.TryGetProperty("required", out var citationRequired)
            && citationRequired.EnumerateArray().Any(property => property.GetString() == "docxLocation"));
    }

    private static async Task NormalizeAsync(
        string repositoryRoot,
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(
            Path.Combine(repositoryRoot, "scripts", "openapi", "snapshot-client-openapi.mjs"));
        startInfo.ArgumentList.Add("--normalize-only");
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(inputPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Node.js OpenAPI 规范化进程。");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "OpenAPI 规范化失败。"
                + Environment.NewLine
                + await standardOutput
                + Environment.NewLine
                + await standardError);
        }
    }

    private static string FindFirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var count = Math.Max(expectedLines.Length, actualLines.Length);
        for (var index = 0; index < count; index++)
        {
            var expectedLine = index < expectedLines.Length ? expectedLines[index] : null;
            var actualLine = index < actualLines.Length ? actualLines[index] : null;
            if (!string.Equals(expectedLine, actualLine, StringComparison.Ordinal))
            {
                return $"第 {index + 1} 行不一致。";
            }
        }
        return "字节内容不同。";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
