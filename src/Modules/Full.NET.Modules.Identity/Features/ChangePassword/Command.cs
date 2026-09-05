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
    : ICommand<ChangePasswordSessionResult>, ITransactionalCommand;

/// <summary>改密成功后签发的新会话令牌。</summary>
internal sealed record ChangePasswordSessionResult(
    TokenResponse Token,
    string RefreshToken,
    string CsrfToken);
