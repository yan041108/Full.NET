using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.RecoverAccount;

internal sealed record RequestCommand(RequestPasswordRecoveryRequest Request)
    : ICommand<AccountChallengeAcceptedResponse>;
