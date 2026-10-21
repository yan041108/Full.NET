using System.Security.Claims;
using Full.NET.Modules.Document.Security;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Document.Http;

/// <summary>HTTP 身份适配；应用边界仅消费可信用户标识。</summary>
internal sealed class HttpDocumentActorContext(IHttpContextAccessor context) : IDocumentActorContext
{
    public Guid? UserId => context.HttpContext?.User is { Identity.IsAuthenticated: true } principal
        && Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;
}
