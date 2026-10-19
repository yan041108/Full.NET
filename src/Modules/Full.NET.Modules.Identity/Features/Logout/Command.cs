using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Http;

/// <summary>模块内命令 DTO；经 Mediator 派发，不直接绑定 HTTP。</summary>
namespace Full.NET.Modules.Identity.Features.Logout;

internal sealed record Command(
    string RefreshToken,
    ClientRequestContext Client) : ICommand<LogoutResult>, ITransactionalCommand;

internal sealed record LogoutResult;
