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
public static class FullNetOpenApiExtensions
{
    public const string DocumentName = "v1";

    public const string ApiTitle = "Full.NET API";

    public const string OpenApiRoutePattern = "/openapi/{documentName}.json";

    public const string OpenApiJsonPath = "/openapi/v1.json";

    public const string ScalarUiPath = "/scalar/v1";

    public static IServiceCollection AddFullNetOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer(ApplyDocumentMetadataAsync);
            options.AddOperationTransformer(ApplyOperationSecurityAsync);
        });
        return services;
    }

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

    private static OpenApiSecurityRequirement CreateSecurityRequirement(
        string schemeName,
        OpenApiDocument document) =>
        new()
        {
            [new OpenApiSecuritySchemeReference(schemeName, document, externalResource: null)] = [],
        };
}
