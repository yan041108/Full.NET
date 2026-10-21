using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Http;

/// <summary>模块内命令 DTO；经 Mediator 派发，不直接绑定 HTTP。</summary>
namespace Full.NET.Modules.Identity.Features.Login;

internal sealed record Command(
    string Username,
    string Password,
    ClientRequestContext Client) : ICommand<LoginSessionResult>, ITransactionalCommand, ITransactionalRejectionPolicy
{
    // 仅此拒绝分支允许保留安全副作用；其余失败不能提交业务写入。
    public bool ShouldCommitRejection(Error error) => error.Code == IdentityErrorCodes.InvalidCredentials;
}

internal sealed record LoginSessionResult(
    TokenResponse Token,
    string RefreshToken,
    string CsrfToken);
