using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.UpdateLocale;

internal sealed record Command(
    string Locale,
    int ProfileVersion,
    ClaimsPrincipal Principal)
    : ICommand<LocalePreferenceResponse>, ITransactionalCommand;
