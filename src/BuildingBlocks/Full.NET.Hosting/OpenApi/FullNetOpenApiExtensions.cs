using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Full.NET.Hosting.OpenApi;

/// <summary>
/// 统一 Host API 的 OpenAPI 文档元数据与安全方案，供 Scalar 与契约测试复用。
/// </summary>
/// <remarks>常量字符串发布后不可改名或删除；新增常量只能追加到末尾。</remarks>
public static class FullNetOpenApiExtensions
{
    /// <summary>OpenAPI 文档名称；用于 MapOpenApi 路由与文档选择器，发布后不可改名。</summary>
    public const string DocumentName = "v1";

    /// <summary>OpenAPI 文档标题；展示在 Scalar 与生成的客户端契约中。</summary>
    public const string ApiTitle = "Full.NET API";

    /// <summary>OpenAPI JSON 端点路由模板；{documentName} 占位符对应 DocumentName。</summary>
    public const string OpenApiRoutePattern = "/openapi/{documentName}.json";

    /// <summary>默认 OpenAPI v1 文档的完整访问路径；供契约测试与外部工具引用。</summary>
    public const string OpenApiJsonPath = "/openapi/v1.json";

    /// <summary>Scalar API 文档 UI 挂载路径；与 OpenApiJsonPath 配对使用。</summary>
    public const string ScalarUiPath = "/scalar/v1";

    /// <summary>为 IServiceCollection 注册 Full.NET OpenAPI 文档，并挂载文档元数据、操作安全与 Schema 转换器。</summary>
    /// <param name="services">服务集合。</param>
    /// <returns>传入的服务集合，便于链式调用。</returns>
    public static IServiceCollection AddFullNetOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer(ApplyDocumentMetadataAsync);
            options.AddOperationTransformer(ApplyOperationSecurityAsync);
            options.AddSchemaTransformer(ApplyJsonOmissionOptionalityAsync);
            options.AddSchemaTransformer(ApplyStableStringEnumAsync);
        });
        return services;
    }

    /// <summary>为 IEndpointRouteBuilder 映射 OpenAPI JSON 端点路由。</summary>
    /// <param name="endpoints">端点路由构建器。</param>
    /// <returns>传入的端点路由构建器，便于链式调用。</returns>
    public static IEndpointRouteBuilder MapFullNetOpenApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
        return endpoints;
    }

    private static Task ApplyDocumentMetadataAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = ApiTitle,
            Version = DocumentName,
            Description =
                "Full.NET 平台 HTTP API 契约。除显式匿名端点外，请在 Scalar 中配置 Bearer JWT 或 ApiKey 后调用受保护接口。"
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "通过 Authorization: Bearer {token} 传递访问令牌。"
        };
        document.Components.SecuritySchemes["ApiKey"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = "Authorization",
            In = ParameterLocation.Header,
            Description = "通过 Authorization: ApiKey {secret} 传递 Host API Key。"
        };
        document.Components.SecuritySchemes["Signature"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = "X-FullNET-Access-Key-Id",
            Description =
                "请求签名认证。需同时提供 X-FullNET-Access-Key-Id、X-FullNET-Timestamp、"
                + "X-FullNET-Nonce、X-FullNET-Signature 与 X-FullNET-Signature-Version=1。"
        };

        return Task.CompletedTask;
    }

    private static Task ApplyOperationSecurityAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (endpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            // 显式空 security：OpenAPI 就绪门禁只接受白名单公开 Operation，
            // 省略 security 会被误判为受保护却未声明方案。
            operation.Security = [];
            return Task.CompletedTask;
        }

        var authorization = endpointMetadata.OfType<IAuthorizeData>().ToArray();
        if (authorization.Length == 0)
        {
            return Task.CompletedTask;
        }

        var document = context.Document
            ?? throw new InvalidOperationException("OpenAPI Operation 转换缺少所属文档。");
        operation.Security =
        [
            CreateSecurityRequirement("Bearer", document),
            CreateSecurityRequirement("ApiKey", document),
        ];
        if (authorization.Any(data => data.Policy?.StartsWith(
                "FullNet.OpenAccess:",
                StringComparison.Ordinal) == true))
        {
            operation.Security.Add(CreateSecurityRequirement("Signature", document));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 将序列化时允许省略的 JSON 属性从 OpenAPI 必填集合中移除。
    /// </summary>
    /// <param name="schema">当前 CLR 类型对应的 OpenAPI Schema。</param>
    /// <param name="context">Schema 转换上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已完成的转换任务。</returns>
    private static Task ApplyJsonOmissionOptionalityAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonPropertyInfo is not null || schema.Required is null)
        {
            return Task.CompletedTask;
        }

        foreach (var property in context.JsonTypeInfo.Properties)
        {
            var ignoreAttribute = property.AttributeProvider?
                .GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true)
                .OfType<JsonIgnoreAttribute>()
                .SingleOrDefault();
            if (ignoreAttribute?.Condition is not (
                    JsonIgnoreCondition.WhenWritingNull
                    or JsonIgnoreCondition.WhenWritingDefault))
            {
                continue;
            }

            // 只要服务端可能按 System.Text.Json 规则省略属性，客户端守卫就不能要求响应中始终存在该键。
            schema.Required.Remove(property.Name);
        }

        return Task.CompletedTask;
    }

    private static Task ApplyStableStringEnumAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var enumAttribute = context.JsonPropertyInfo?
            .AttributeProvider?
            .GetCustomAttributes(typeof(FullNetOpenApiStringEnumAttribute), inherit: true)
            .OfType<FullNetOpenApiStringEnumAttribute>()
            .SingleOrDefault();
        if (enumAttribute is not null)
        {
            schema.Enum = enumAttribute.Values
                .Select(value => (JsonNode)JsonValue.Create(value)!)
                .ToList();
        }

        return Task.CompletedTask;
    }

    private static OpenApiSecurityRequirement CreateSecurityRequirement(
        string schemeName,
        OpenApiDocument document) =>
        new()
        {
            [new OpenApiSecuritySchemeReference(schemeName, document, externalResource: null)] = [],
        };
}
