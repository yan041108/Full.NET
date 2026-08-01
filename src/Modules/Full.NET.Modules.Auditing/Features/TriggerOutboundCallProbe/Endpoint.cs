using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Features.WriteOutboundCallLogs;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Full.NET.Modules.Auditing.Features.TriggerOutboundCallProbe;

/// <summary>
/// 仅 Testing 宿主暴露的出站审计探针，用于集成测试验证脱敏写入；不得进入非 Testing 环境。
/// </summary>
internal static class Endpoint
{
    public const string ProbeProviderKey = "auditing.outbound_probe";

    public const string ProbeOperationKey = "record_probe";

    public static void Map(IEndpointRouteBuilder endpoints, IWebHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Testing"))
        {
            return;
        }

        endpoints.MapPost(
                "/api/v1/auditing/outbound-call-probes",
                async (
                    OutboundCallAuditProbeRequest request,
                    OutboundCallAuditHandler handler,
                    CancellationToken cancellationToken) =>
                {
                    await handler.RecordAsync(request.Audit, cancellationToken)
                        .ConfigureAwait(false);
                    return Results.NoContent();
                })
            .WithTags("Auditing")
            .RequireAuthorization(FullNetPermissionPolicies.For(OutboundCallLogPermissions.Read));
    }
}
