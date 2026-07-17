using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Http;

namespace Full.NET.Modules.Identity.Features.Login;

internal sealed record Command(
    string Username,
    string Password,
    ClientRequestContext Client) : ICommand<LoginSessionResult>, ITransactionalCommand;

internal sealed record LoginSessionResult(
    TokenResponse Token,
    string RefreshToken,
    string CsrfToken);
