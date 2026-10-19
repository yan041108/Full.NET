using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

/// <summary>模块内命令 DTO；经 Mediator 派发，不直接绑定 HTTP。</summary>
namespace Full.NET.Modules.Identity.Features.RecoverAccount;

internal sealed record RequestCommand(RequestPasswordRecoveryRequest Request)
    : ICommand<AccountChallengeAcceptedResponse>;
