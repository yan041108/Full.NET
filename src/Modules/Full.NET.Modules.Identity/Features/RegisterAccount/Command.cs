using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

/// <summary>模块内命令 DTO；经 Mediator 派发，不直接绑定 HTTP。</summary>
namespace Full.NET.Modules.Identity.Features.RegisterAccount;

internal sealed record Command(RegisterAccountRequest Request) : ICommand<RegisterAccountResponse>;
