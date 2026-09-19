using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.RegisterAccount;

internal sealed record Command(RegisterAccountRequest Request) : ICommand<RegisterAccountResponse>;
