using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Identity;

internal static class IdentityOidcMultiInstanceTestSupport
{
    internal const string DataProtectionPassword = "FullNet-Test-Only!";

    // 固定 256 位测试密钥，确保多实例工厂能互解 OpenIddict refresh token。
    internal const string SharedEncryptionKeyBase64 =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    internal static (string RootPath, string KeyRingPath, string CertificatePath) CreateDataProtectionAssets()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "fullnet-oidc-mi-" + Guid.NewGuid().ToString("N"));
        var keyRing = Path.Combine(root, "keys");
        var certificate = Path.Combine(root, "active.pfx");
        Directory.CreateDirectory(keyRing);
        CreateSelfSignedPfx(certificate, DataProtectionPassword, "CN=Full.NET.DP.Active");
        return (root, keyRing, certificate);
    }

    /// <summary>
    /// 使用共享 OIDC 签名密钥、加密密钥与 DataProtection 密钥环启动两个隔离 Host 工厂。
    /// </summary>
    internal static async Task UsingConfiguredPairAsync(
        DatabaseProvider provider,
        string connectionString,
        string signingKeyId,
        Func<FullNetApiFactory, FullNetApiFactory, CancellationToken, Task> scenario,
        CancellationToken cancellationToken = default)
    {
        var dataProtectionAssets = CreateDataProtectionAssets();
        using var signingKey = RSA.Create(3072);
        var settings = BuildFactorySettings(
            signingKey,
            signingKeyId,
            dataProtectionAssets.KeyRingPath,
            dataProtectionAssets.CertificatePath);
        try
        {
            using var primaryFactory = new FullNetApiFactory(provider, connectionString, settings);
            using var secondaryFactory = primaryFactory.CreateIsolatedFactory();
            await primaryFactory.InitializeAsync(cancellationToken);
            await secondaryFactory.InitializeAsync(cancellationToken);
            await scenario(primaryFactory, secondaryFactory, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(dataProtectionAssets.RootPath);
        }
    }

    internal static void TryDeleteDirectory(string rootPath)
    {
        try
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal static IReadOnlyDictionary<string, string?> BuildFactorySettings(
        RSA signingKey,
        string signingKeyId,
        string keyRingPath,
        string certificatePath)
    {
        var settings = new Dictionary<string, string?>(IdentityOidcProtocolAssertions.Settings)
        {
            ["Identity:Oidc:AllowDevelopmentEphemeralSigningKey"] = "false",
            ["Identity:Oidc:EncryptionKeyBase64"] = SharedEncryptionKeyBase64,
            ["Identity:Oidc:ActiveSigningKeyId"] = signingKeyId,
            [$"Identity:Oidc:SigningKeys:{signingKeyId}:PublicKeyPem"] = signingKey.ExportRSAPublicKeyPem(),
            [$"Identity:Oidc:SigningKeys:{signingKeyId}:PrivateKeyPem"] = signingKey.ExportRSAPrivateKeyPem(),
            ["DataProtection:ApplicationName"] = "Full.NET.MultiInstance",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = DataProtectionPassword,
        };
        return settings;
    }


    internal static IReadOnlyDictionary<string, string?> BuildDualKeyFactorySettings(
        RSA keyA,
        RSA keyB,
        string activeKeyId,
        string keyRingPath,
        string certificatePath)
    {
        var settings = new Dictionary<string, string?>(
            IdentityOidcSigningKeyRotationAssertions.BuildDualKeySettings(keyA, keyB, activeKeyId))
        {
            ["Identity:Oidc:EncryptionKeyBase64"] = SharedEncryptionKeyBase64,
            ["DataProtection:ApplicationName"] = "Full.NET.MultiInstance",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = DataProtectionPassword,
        };
        return settings;
    }


    internal static IReadOnlyDictionary<string, string?> BuildDualPrivateKeyFactorySettings(
        RSA keyA,
        RSA keyB,
        string activeKeyId,
        string keyRingPath,
        string certificatePath)
    {
        var settings = new Dictionary<string, string?>(
            IdentityOidcSigningKeyRotationAssertions.BuildDualPrivateKeySettings(keyA, keyB, activeKeyId))
        {
            ["DataProtection:ApplicationName"] = "Full.NET.MultiInstance",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = DataProtectionPassword,
        };
        return settings;
    }

    internal static IReadOnlyDictionary<string, string?> BuildSinglePrivateKeyFactorySettings(
        RSA signingKey,
        string signingKeyId,
        string keyRingPath,
        string certificatePath)
    {
        var settings = new Dictionary<string, string?>(
            IdentityOidcSigningKeyRetirementAssertions.BuildSinglePrivateKeySettings(signingKey, signingKeyId))
        {
            ["Identity:Oidc:EncryptionKeyBase64"] = SharedEncryptionKeyBase64,
            ["DataProtection:ApplicationName"] = "Full.NET.MultiInstance",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = DataProtectionPassword,
        };
        return settings;
    }

    private static void CreateSelfSignedPfx(string path, string password, string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            subject,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature,
                critical: true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    }
}