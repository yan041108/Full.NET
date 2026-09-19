using Full.NET.Abstractions.Messaging;

namespace Full.NET.Modules.Identity.Features.RecoverAccount;

internal sealed record ConfirmCommand(Contracts.ConfirmPasswordRecoveryRequest Request) : ICommand<bool>;
