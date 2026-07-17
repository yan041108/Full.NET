using System.Security.Cryptography;
using Full.NET.Modules.Identity.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Full.NET.Modules.Identity.Security;

internal sealed class RsaSigningKeyRing : IDisposable
{
    private readonly List<RSA> _ownedKeys = [];

    public RsaSigningKeyRing(
        IOptions<IdentityOptions> options,
        ILogger<RsaSigningKeyRing> logger)
    {
        var settings = options.Value;
        if (settings.SigningKeys.Count == 0)
        {
            if (!settings.AllowDevelopmentEphemeralSigningKey)
            {
                throw new InvalidOperationException(
                    "Identity signing keys are not configured.");
            }

            var rsa = RSA.Create(3072);
            _ownedKeys.Add(rsa);
            var securityKey = new RsaSecurityKey(rsa)
            {
                KeyId = $"dev-{Guid.NewGuid():N}",
            };
            SigningCredentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.RsaSha256);
            ValidationKeys = [securityKey];
            logger.LogWarning(
                "Identity is using an ephemeral development signing key with KeyId {KeyId}",
                securityKey.KeyId);
            return;
        }

        var validationKeys = new List<SecurityKey>();
        SigningCredentials? signingCredentials = null;
        foreach (var pair in settings.SigningKeys)
        {
            var rsa = RSA.Create();
            _ownedKeys.Add(rsa);
            if (string.Equals(
                pair.Key,
                settings.ActiveKeyId,
                StringComparison.Ordinal))
            {
                rsa.ImportFromPem(NormalizePem(pair.Value.PrivateKeyPem));
            }
            else
            {
                rsa.ImportFromPem(NormalizePem(pair.Value.PublicKeyPem));
            }

            var securityKey = new RsaSecurityKey(rsa) { KeyId = pair.Key };
            validationKeys.Add(securityKey);
            if (string.Equals(
                pair.Key,
                settings.ActiveKeyId,
                StringComparison.Ordinal))
            {
                signingCredentials = new SigningCredentials(
                    securityKey,
                    SecurityAlgorithms.RsaSha256);
            }
        }

        SigningCredentials = signingCredentials ?? throw new InvalidOperationException(
            "Identity ActiveKeyId does not identify a configured private signing key.");
        ValidationKeys = validationKeys;
    }

    public SigningCredentials SigningCredentials { get; }

    public IReadOnlyCollection<SecurityKey> ValidationKeys { get; }

    public void Dispose()
    {
        foreach (var key in _ownedKeys)
        {
            key.Dispose();
        }

        _ownedKeys.Clear();
    }

    private static ReadOnlySpan<char> NormalizePem(string pem) =>
        pem.Replace("\\n", "\n", StringComparison.Ordinal).AsSpan();
}
