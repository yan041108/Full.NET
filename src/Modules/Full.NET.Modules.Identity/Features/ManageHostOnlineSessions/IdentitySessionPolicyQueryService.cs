using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;

/// <summary>读取当前 Host 登录会话策略配置。</summary>
internal sealed class IdentitySessionPolicyQueryService(IOptions<IdentityOptions> options)
{
    /// <summary>返回当前生效的登录会话并发策略。</summary>
    public Result<IdentitySessionPolicyResponse> GetPolicy() =>
        Result<IdentitySessionPolicyResponse>.Success(
            new IdentitySessionPolicyResponse(options.Value.SessionLoginPolicy));
}
