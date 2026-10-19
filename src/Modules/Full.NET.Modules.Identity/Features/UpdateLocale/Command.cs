using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

/// <summary>模块内命令 DTO；经 Mediator 派发，不直接绑定 HTTP。</summary>
namespace Full.NET.Modules.Identity.Features.UpdateLocale;

internal sealed record Command(
    string Locale,
    int ProfileVersion,
    ClaimsPrincipal Principal)
    : ICommand<LocalePreferenceResponse>, ITransactionalCommand;
