using System.Security.Claims;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 在令牌密码学验证完成后补充会话实时校验，避免仅依赖访问令牌有效期延迟撤销。
/// </summary>
internal sealed class FullNetJwtBearerEvents(
    AccessSessionValidator sessionValidator,
    ICurrentTenantContextWriter tenantContextWriter,
    IOptions<IdentityOidcOptions> oidcOptions,
    Microsoft.Extensions.Configuration.IConfiguration configuration) : JwtBearerEvents
{
    internal static readonly object McpAudienceKey = new();

    public override Task Challenge(JwtBearerChallengeContext context)
    {
        if (context.Scheme.Name != McpResourceAuthentication.Scheme) return Task.CompletedTask;
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var resource = configuration["FullNet:Ai:Mcp:ResourceIdentifier"];
        if (Uri.TryCreate(resource, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            // 从可信资源配置派生发现位置，不使用未可信的请求 Host。
            var metadata = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/.well-known/oauth-protected-resource/ai/mcp");
            context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{metadata.AbsoluteUri}\"";
        }
        else context.Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        EstablishHostContextForOidcAccessToken(context.Principal);
        var audience = context.Scheme.Name == McpResourceAuthentication.Scheme
            ? configuration["FullNet:Ai:Mcp:ResourceIdentifier"] ?? "fullnet://ai/mcp"
            : null;
        var isValid = context.Principal is not null
            && await sessionValidator.IsValidAsync(
                    context.Principal,
                    context.HttpContext.RequestAborted, audience)
                .ConfigureAwait(false);
        if (!isValid)
        {
            // 失败原因不进入认证响应，避免向调用方泄露账号或会话状态细节。
            context.Fail("The access session is no longer active.");
            return;
        }
        if (audience is not null) context.HttpContext.Items[McpAudienceKey] = audience;
        if (!oidcOptions.Value.Enable || context.SecurityToken.Issuer != oidcOptions.Value.Issuer) return;
        var keyId = context.SecurityToken switch
        {
            JsonWebToken token => token.Kid,
            System.IdentityModel.Tokens.Jwt.JwtSecurityToken token => token.Header.Kid,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(keyId)) { context.Fail("OIDC signing authority is unavailable."); return; }
        try
        {
            // EventsType 覆盖 Options.Events；强一致退役检查必须位于实际运行的类型事件中。
            if (!await context.HttpContext.RequestServices.GetRequiredService<IdentityOidcKeyGovernance>()
                .IsValidationKeyAllowedAsync(keyId, context.HttpContext.RequestAborted).ConfigureAwait(false))
                context.Fail("OIDC signing authority is unavailable.");
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Full.NET.Abstractions.Results.ServiceCapacityExceededException) { throw; }
        catch (Exception) { context.Fail("OIDC signing authority is unavailable."); }
    }

    private void EstablishHostContextForOidcAccessToken(ClaimsPrincipal? principal)
    {
        if (principal is null)
        {
            return;
        }

        var issuer = principal.FindFirstValue(JwtRegisteredClaimNames.Iss);
        var hasApplicationSession = !string.IsNullOrWhiteSpace(
            principal.FindFirstValue(FullNetIdentityClaimTypes.ApplicationSessionId));
        if (!hasApplicationSession
            && (!oidcOptions.Value.Enable
                || string.IsNullOrWhiteSpace(oidcOptions.Value.Issuer)
                || !string.Equals(issuer, oidcOptions.Value.Issuer, StringComparison.Ordinal)))
        {
            return;
        }

        // 当前 OIDC 授权流仅签发 Host 会话；在 HostOnly 会话 SQL 查询前必须切换到 Host 上下文。
        tenantContextWriter.SetHost();
    }
}
