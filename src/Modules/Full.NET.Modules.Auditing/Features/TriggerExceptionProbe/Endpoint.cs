using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Auditing.Features.TriggerExceptionProbe;

/// <summary>
/// 仅 Testing 宿主暴露的异常探针，用于集成测试验证异常日志写入；不得进入非 Testing 环境。
/// </summary>
internal static class Endpoint
{
    /// <summary>稳定探针消息，供 Integration 断言匹配。</summary>
    public const string ProbeMessage = "auditing.exception_probe";

    public static void Map(IEndpointRouteBuilder endpoints, IWebHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing"))
        {
            return;
        }

        endpoints.MapPost(
                "/api/v1/auditing/exception-probes",
                (HttpContext _) => throw new InvalidOperationException(ProbeMessage))
            .WithTags("Auditing")
            .RequireAuthorization(FullNetPermissionPolicies.For(ExceptionLogPermissions.Read));
    }
}
