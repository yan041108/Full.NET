using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Http;

namespace Full.NET.Modules.Identity.Features.RefreshSession;

internal sealed record Command(
    string RefreshToken,
    ClientRequestContext Client) : ICommand<RefreshSessionResult>, ITransactionalCommand;

internal sealed record RefreshSessionResult(
    TokenResponse Token,
    string RefreshToken,
    string CsrfToken);
