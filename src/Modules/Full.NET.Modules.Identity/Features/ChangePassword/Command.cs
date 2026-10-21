using Full.NET.Abstractions.Results;
using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Http;

namespace Full.NET.Modules.Identity.Features.ChangePassword;

/// <summary>当前用户自助改密命令。</summary>
internal sealed record Command(
    string CurrentPassword,
    string NewPassword,
    ClaimsPrincipal Principal,
    ClientRequestContext Client)
    : ICommand<ChangePasswordSessionResult>, ITransactionalCommand, ITransactionalRejectionPolicy
{
    // 仅此拒绝分支允许保留安全副作用；其余失败不能提交业务写入。
    public bool ShouldCommitRejection(Error error) => error.Code == IdentityErrorCodes.CurrentPasswordInvalid;
}

/// <summary>改密成功后签发的新会话令牌。</summary>
internal sealed record ChangePasswordSessionResult(
    TokenResponse Token,
    string RefreshToken,
    string CsrfToken);
