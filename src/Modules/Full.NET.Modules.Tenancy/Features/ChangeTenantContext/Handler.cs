using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ChangeTenantContext;

/// <summary>
/// 已授权用户切换租户上下文处理器。
/// 事务与安全顺序：
/// 1) 请求指定 TenantId 时按 ID 解析租户并校验 IsActive，否则视为切回 Host Scope；
/// 2) 委派 IIdentitySessionContextService.ChangeAsync 做后续原子步骤：
///    写入领域审计 → 轮换用户 SecurityStamp/刷新会话 → 重新签发包含新 TenantId Claim
///    的 Access Token + 按新 Scope 重新投影 Cookie；
/// 3) 要求操作者原本必须是 Host 作用域已认证主体；普通租户会话不得跨租户提权。
/// </summary>
internal sealed class Handler(
    ITenantResolver tenantResolver,
    IIdentitySessionContextService identitySessionContextService)
    : ICommandHandler<Command, TenantContextTokenResponse>
{
    /// <summary>
    /// 执行租户上下文切换；返回包含新 Access/Refresh/CSRF Token 的响应。
    /// </summary>
    /// <param name="command">携带原用户 Principal 与目标 TenantId（可空表示切回 Host）。</param>
    public async Task<Result<TenantContextTokenResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        VerifiedTenantContext? verifiedTenant = null;
        if (command.TenantId.HasValue)
        {
            var tenant = await tenantResolver.ResolveByIdAsync(
                    command.TenantId.Value,
                    cancellationToken)
                .ConfigureAwait(false);
            if (tenant is not { IsActive: true })
            {
                return Result<TenantContextTokenResponse>.Failure(new Error(
                    Code: TenancyErrorCodes.ContextNotFound,
                    Message: "The requested tenant context was not found.",
                    Type: ErrorType.NotFound));
            }

            verifiedTenant = new VerifiedTenantContext(
                tenant.Id,
                tenant.Identifier,
                tenant.Name,
                tenant.Domain);
        }

        return await identitySessionContextService.ChangeAsync(
                command.Principal,
                verifiedTenant,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
