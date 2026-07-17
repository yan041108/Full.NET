using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Http;

namespace Full.NET.Modules.Identity.Features.Logout;

internal sealed record Command(
    string RefreshToken,
    ClientRequestContext Client) : ICommand<LogoutResult>, ITransactionalCommand;

internal sealed record LogoutResult;
