using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Messaging.Auditing;

/// <summary>从已认证主体读取人工运维身份；不接收客户端提交的操作者字段。</summary>
internal sealed record MessagingOperationActor(Guid UserId, string DisplayName)
{
    public static MessagingOperationActor? Resolve(IHttpContextAccessor? accessor)
    {
        var principal = accessor?.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(
            principal.FindFirstValue(FullNetIdentityClaimTypes.Subject)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || id == Guid.Empty)
            return null;
        var name = principal.Identity.Name ?? principal.FindFirstValue("name") ?? id.ToString("D");
        return new MessagingOperationActor(id, name.Length > 128 ? name[..128] : name);
    }

    public static Error? Validate(MessagingOperationActor? actor, string? reason)
    {
        if (actor is null)
            return new Error("messaging.operations.actor_required", "缺少可信的运维操作者身份。", ErrorType.Unauthorized);
        return string.IsNullOrWhiteSpace(reason) || reason.Length > 512
            ? new Error("messaging.operations.reason_invalid", "请提供不超过 512 字的运维理由。", ErrorType.Validation)
            : null;
    }
}
