using Full.NET.Modules.Identity.Domain;

namespace Full.NET.Modules.Identity.Security;

internal interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(
        IdentityUser user,
        Guid sessionId,
        Guid? activeTenantId,
        IReadOnlyCollection<string> permissions);
}

internal sealed record IssuedAccessToken(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc);
