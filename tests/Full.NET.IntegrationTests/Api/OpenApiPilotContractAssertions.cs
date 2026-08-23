using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

/// <summary>
/// 对进入客户端生成试点的 Operation 执行精确线协议断言。
/// </summary>
internal static class OpenApiPilotContractAssertions
{
    public static void AssertOperation(
        JsonElement document,
        string path,
        HttpMethod method,
        string operationId,
        string primaryTag,
        int successStatus,
        string? expectedResponseMediaType,
        string? expectedRequestMediaType = null,
        bool allowsSignature = false,
        bool isPublic = false)
    {
        var operation = document
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method.Method.ToLowerInvariant());

        Assert.AreEqual(operationId, operation.GetProperty("operationId").GetString());
        var tags = operation.GetProperty("tags")
            .EnumerateArray()
            .Select(tag => tag.GetString())
            .ToArray();
        CollectionAssert.AreEqual(new[] { primaryTag }, tags);

        AssertDeclaredSecurity(document, operation, method, path, allowsSignature, isPublic);
        AssertProblemDetails(operation, method, path);
        AssertSuccessResponse(
            document,
            operation,
            method,
            path,
            successStatus,
            expectedResponseMediaType);

        if (expectedRequestMediaType is not null)
        {
            var requestContent = operation
                .GetProperty("requestBody")
                .GetProperty("content");
            Assert.IsTrue(
                requestContent.TryGetProperty(expectedRequestMediaType, out var requestMedia),
                $"{method.Method} {path} 缺少 {expectedRequestMediaType} 请求声明。");
            AssertRuntimeSchema(requestMedia.GetProperty("schema"), method, path);
        }
    }

    private static void AssertDeclaredSecurity(
        JsonElement document,
        JsonElement operation,
        HttpMethod method,
        string path,
        bool allowsSignature,
        bool isPublic)
    {
        Assert.IsTrue(
            operation.TryGetProperty("security", out var security)
            && security.ValueKind == JsonValueKind.Array,
            $"{method.Method} {path} 缺少 Operation 安全声明。");

        if (isPublic)
        {
            Assert.AreEqual(
                0,
                security.GetArrayLength(),
                $"{method.Method} {path} 的公开 Operation 必须显式声明空 security 数组。");
            return;
        }

        Assert.IsTrue(
            security.GetArrayLength() > 0,
            $"{method.Method} {path} 缺少 Operation 安全声明。");

        var declaredSchemes = security
            .EnumerateArray()
            .SelectMany(requirement => requirement.EnumerateObject())
            .Select(requirement => requirement.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedSchemes = allowsSignature
            ? new[] { "ApiKey", "Bearer", "Signature" }
            : new[] { "ApiKey", "Bearer" };
        CollectionAssert.AreEqual(expectedSchemes, declaredSchemes);

        var registeredSchemes = document
            .GetProperty("components")
            .GetProperty("securitySchemes");
        foreach (var scheme in declaredSchemes)
        {
            Assert.IsTrue(
                registeredSchemes.TryGetProperty(scheme, out _),
                $"{method.Method} {path} 引用了未注册的安全方案 {scheme}。");
        }
    }

    private static void AssertProblemDetails(
        JsonElement operation,
        HttpMethod method,
        string path)
    {
        var responses = operation.GetProperty("responses");
        foreach (var status in new[] { "401", "403" })
        {
            Assert.IsTrue(
                responses.TryGetProperty(status, out var response),
                $"{method.Method} {path} 缺少 {status} ProblemDetails 响应。");
            var content = response.GetProperty("content");
            Assert.IsTrue(
                content.TryGetProperty("application/problem+json", out var problemMedia),
                $"{method.Method} {path} 的 {status} 响应不是 application/problem+json。");
            AssertRuntimeSchema(problemMedia.GetProperty("schema"), method, path);
        }
    }

    private static void AssertSuccessResponse(
        JsonElement document,
        JsonElement operation,
        HttpMethod method,
        string path,
        int successStatus,
        string? expectedMediaType)
    {
        var response = operation
            .GetProperty("responses")
            .GetProperty(successStatus.ToString());
        if (expectedMediaType is null)
        {
            Assert.IsFalse(
                response.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Object
                && content.EnumerateObject().Any(),
                $"{method.Method} {path} 的 {successStatus} 响应不得声明 content。");
            return;
        }

        var responseContent = response.GetProperty("content");
        Assert.IsTrue(
            responseContent.TryGetProperty(expectedMediaType, out var media),
            $"{method.Method} {path} 缺少 {expectedMediaType} 成功响应。");
        var schema = media.GetProperty("schema");
        AssertRuntimeSchema(schema, method, path);
        if (expectedMediaType == "application/octet-stream")
        {
            var resolvedSchema = ResolveLocalSchema(document, schema);
            Assert.AreEqual("string", resolvedSchema.GetProperty("type").GetString());
            Assert.AreEqual("binary", resolvedSchema.GetProperty("format").GetString());
        }
    }

    private static JsonElement ResolveLocalSchema(JsonElement document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var referenceElement))
        {
            return schema;
        }

        var reference = referenceElement.GetString();
        Assert.IsNotNull(reference);
        Assert.IsTrue(reference.StartsWith("#/", StringComparison.Ordinal));
        var current = document;
        foreach (var segment in reference[2..].Split('/'))
        {
            current = current.GetProperty(segment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal));
        }

        return current;
    }

    private static void AssertRuntimeSchema(
        JsonElement schema,
        HttpMethod method,
        string path)
    {
        Assert.IsTrue(
            schema.TryGetProperty("$ref", out _)
            || schema.TryGetProperty("type", out _),
            $"{method.Method} {path} 的 Schema 缺少显式 type 或 $ref。");
        if (schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "array")
        {
            Assert.IsTrue(
                schema.TryGetProperty("items", out _),
                $"{method.Method} {path} 的数组 Schema 缺少 items。");
        }
    }
}
