using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy;

/// <summary>
/// 在 BeforeAuthorization 阶段建立 ICurrentTenant 上下文的管道中间件。
/// 解析顺序不变量（严格按优先级）：
/// 1) 请求非 /api → 跳过，交给 SPA 静态资源处理；
/// 2) 已认证 + 存在 TenantId Claim（从切换写入的新令牌）→ 按 Claim 值解析并校验 Claim 与 Domain 是否一致；
/// 3) 已认证但无 TenantId Claim → HostDomains 命中则进入 Host Scope，否则拒绝上下文不匹配；
/// 4) 未认证 + HostDomains 命中 → Host Scope；
/// 5) 未认证 + 非 HostDomains → 按域名解析租户进入对应 Scope，无租户返回 404 HostNotFound。
/// 安全边界：TenantId Claim 必须为单值且 Guid，重复或畸形值一律返回 403 避免歧义解释；
/// finally 中显式 Clear，防止 Scoped 回收延迟导致上下文残留到下一次请求复用。
/// </summary>
internal sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    private const string TenantItemKey = "FullNet.TenantId";

    public async Task InvokeAsync(
        HttpContext httpContext,
        ITenantResolver resolver,
        CurrentTenantAccessor currentTenant,
        IOptions<TenancyOptions> options,
        IApiResultMapper resultMapper)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            await next(httpContext).ConfigureAwait(false);
            return;
        }

        var host = httpContext.Request.Host.Host;
        var isHostDomain = options.Value.HostDomains.Contains(
            host,
            StringComparer.OrdinalIgnoreCase);
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaims = httpContext.User
                .FindAll(FullNetIdentityClaimTypes.TenantId)
                .Select(claim => claim.Value)
                .ToArray();
            if (tenantClaims.Length > 0)
            {
                await ResolveAuthenticatedTenantAsync(
                        httpContext,
                        resolver,
                        currentTenant,
                        resultMapper,
                        host,
                        isHostDomain,
                        tenantClaims)
                    .ConfigureAwait(false);
                return;
            }

            if (!isHostDomain)
            {
                await WriteFailureAsync(
                        httpContext,
                        resultMapper,
                        TenancyErrorCodes.ContextMismatch,
                        "The authenticated context does not match the request host.",
                        ErrorType.Forbidden)
                    .ConfigureAwait(false);
                return;
            }

            await InvokeInHostScopeAsync(httpContext, currentTenant)
                .ConfigureAwait(false);
            return;
        }

        if (isHostDomain)
        {
            await InvokeInHostScopeAsync(httpContext, currentTenant)
                .ConfigureAwait(false);
            return;
        }

        var tenant = string.IsNullOrWhiteSpace(host)
            ? null
            : await resolver
                .ResolveByDomainAsync(host, httpContext.RequestAborted)
                .ConfigureAwait(false);
        if (tenant is null || !tenant.IsActive)
        {
            await WriteFailureAsync(
                    httpContext,
                    resultMapper,
                    TenancyErrorCodes.HostNotFound,
                    "No active tenant is configured for this host.",
                    ErrorType.NotFound)
                .ConfigureAwait(false);
            return;
        }

        await InvokeInTenantScopeAsync(httpContext, currentTenant, tenant)
            .ConfigureAwait(false);
    }

    private async Task ResolveAuthenticatedTenantAsync(
        HttpContext httpContext,
        ITenantResolver resolver,
        CurrentTenantAccessor currentTenant,
        IApiResultMapper resultMapper,
        string host,
        bool isHostDomain,
        IReadOnlyList<string> tenantClaims)
    {
        // 单值 Claim 是令牌签发器的不变量；重复或畸形值必须拒绝，避免不同组件选择不同值。
        if (tenantClaims.Count != 1 ||
            !Guid.TryParse(tenantClaims[0], out var tenantId))
        {
            await WriteFailureAsync(
                    httpContext,
                    resultMapper,
                    TenancyErrorCodes.ContextMismatch,
                    "The authenticated tenant context is invalid.",
                    ErrorType.Forbidden)
                .ConfigureAwait(false);
            return;
        }

        var tenant = await resolver
            .ResolveByIdAsync(tenantId, httpContext.RequestAborted)
            .ConfigureAwait(false);
        if (tenant is null || !tenant.IsActive)
        {
            await WriteFailureAsync(
                    httpContext,
                    resultMapper,
                    TenancyErrorCodes.ContextMismatch,
                    "The authenticated tenant context is unavailable.",
                    ErrorType.Forbidden)
                .ConfigureAwait(false);
            return;
        }

        if (!isHostDomain)
        {
            var domainTenant = string.IsNullOrWhiteSpace(host)
                ? null
                : await resolver
                    .ResolveByDomainAsync(host, httpContext.RequestAborted)
                    .ConfigureAwait(false);
            if (domainTenant is null ||
                !domainTenant.IsActive ||
                domainTenant.Id != tenant.Id)
            {
                await WriteFailureAsync(
                        httpContext,
                        resultMapper,
                        TenancyErrorCodes.ContextMismatch,
                        "The authenticated context does not match the request host.",
                        ErrorType.Forbidden)
                    .ConfigureAwait(false);
                return;
            }
        }

        await InvokeInTenantScopeAsync(httpContext, currentTenant, tenant)
            .ConfigureAwait(false);
    }

    private async Task InvokeInHostScopeAsync(
        HttpContext httpContext,
        CurrentTenantAccessor currentTenant)
    {
        currentTenant.SetHost();
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private async Task InvokeInTenantScopeAsync(
        HttpContext httpContext,
        CurrentTenantAccessor currentTenant,
        TenantSummary tenant)
    {
        httpContext.Items[TenantItemKey] = tenant.Id;
        currentTenant.SetTenant(new TenantContext(
            tenant.Id,
            tenant.Identifier,
            tenant.Name));
        try
        {
            await next(httpContext).ConfigureAwait(false);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static Task WriteFailureAsync(
        HttpContext httpContext,
        IApiResultMapper resultMapper,
        string code,
        string message,
        ErrorType errorType) =>
        resultMapper
            .Map(
                Result<object?>.Failure(new Error(
                    Code: code,
                    Message: message,
                    Type: errorType)),
                httpContext)
            .ExecuteAsync(httpContext);
}
