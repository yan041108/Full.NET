using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Oidc;

/// <summary>中心登录的第二因子校验；启用 TOTP 后不得退回密码单因子，恢复码只消费一次。</summary>
internal sealed class IdentityOidcMfaVerifier(TotpSecretProtector protector, ICommandExecutor commands,
    MfaRecoveryCodeService recoveryCodes, IClock clock)
{
    public async Task<string[]?> VerifyAsync(IdentityUserTotpRecord record, string? proof, CancellationToken token)
    {
        if (!record.IsEnabled) return ["pwd"];
        if (string.IsNullOrWhiteSpace(proof)) return null;
        if (proof.Length != TotpAlgorithm.Digits)
            return (await recoveryCodes.ConsumeAsync(record.UserId, proof, token).ConfigureAwait(false)).IsSuccess
                ? ["pwd", "urn:fullnet:amr:recovery-code"] : null;
        byte[] key;
        try { key = TotpAlgorithm.DecodeSharedSecret(protector.Unprotect(record.SecretProtected)); }
        catch (Exception error) when (error is CryptographicException or FormatException) { return null; }
        try
        {
            long? accepted = null;
            var current = clock.UtcNow.ToUnixTimeSeconds() / TotpAlgorithm.StepSeconds;
            for (var step = current - 1; step <= current + 1; step++)
                if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(TotpAlgorithm.ComputeCode(key, step)),
                    Encoding.ASCII.GetBytes(proof))) accepted = step;
            if (accepted is null) return null;
            var count = await commands.ExecuteAsync(IdentityOidcInteractionSql.AcceptTotpCounter,
                IdentitySqlParameters.Create(("UserId", record.UserId), ("Counter", accepted),
                    ("Version", record.Version), ("Now", clock.UtcNow)), token).ConfigureAwait(false);
            return count == 1 ? ["pwd", "otp"] : null;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
