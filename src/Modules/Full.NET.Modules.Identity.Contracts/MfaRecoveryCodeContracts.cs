namespace Full.NET.Modules.Identity.Contracts;

public sealed record RegenerateMfaRecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record ConsumeMfaRecoveryCodeRequest(string RecoveryCode);

public sealed record ConsumeMfaRecoveryCodeResponse(bool Consumed);
