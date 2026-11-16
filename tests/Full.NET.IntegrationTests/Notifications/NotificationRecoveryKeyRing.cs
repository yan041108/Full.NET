using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Full.NET.Hosting.Security;
using Full.NET.Modules.Notifications.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>以隔离备份介质恢复证书保护的密钥环，覆盖实际收件端点保护器。</summary>
internal sealed class NotificationRecoveryKeyRing : IDisposable
{
    private const string ApplicationName = "Full.NET.NotificationRecovery";
    private const string Probe = "notification-keyring-recovery-probe";
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("fullnet-notification-keyring-");
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public Dictionary<string, string?> SourceSettings => SettingsFor(Path.Combine(root.FullName, "source", "keys"),
        Path.Combine(root.FullName, "source", "active.pfx"));

    public ServiceProvider OpenSource()
    {
        var source = root.CreateSubdirectory("source");
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Full.NET Notification Recovery Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllBytes(CertificatePath(source), certificate.Export(X509ContentType.Pfx, password));
        return Open(source.CreateSubdirectory("keys"), CertificatePath(source));
    }

    public ServiceProvider Restore(IDataProtectionProvider sourceProtection)
    {
        var protectedProbe = new NotificationRecipientEndpointProtector(sourceProtection).Protect(Probe);
        var source = new DirectoryInfo(Path.Combine(root.FullName, "source"));
        var keyFiles = Directory.GetFiles(Path.Combine(source.FullName, "keys"), "key-*.xml");
        Assert.IsGreaterThan(0, keyFiles.Length);
        foreach (var keyFile in keyFiles)
            Assert.IsTrue(XDocument.Load(keyFile).Descendants().Any(element => element.Name.LocalName == "encryptedSecret"));

        var backup = Path.Combine(root.FullName, "data-protection.zip");
        ZipFile.CreateFromDirectory(source.FullName, backup);
        var target = root.CreateSubdirectory("restored");
        ZipFile.ExtractToDirectory(backup, target.FullName);
        using var missingKeys = Open(root.CreateSubdirectory("empty"), CertificatePath(target));
        var unrelatedProtector = new NotificationRecipientEndpointProtector(missingKeys.GetRequiredService<IDataProtectionProvider>());
        Assert.ThrowsExactly<CryptographicException>(() => unrelatedProtector.Unprotect(protectedProbe));
        var restored = Open(new DirectoryInfo(Path.Combine(target.FullName, "keys")), CertificatePath(target));
        try
        {
            var restoredProtector = new NotificationRecipientEndpointProtector(restored.GetRequiredService<IDataProtectionProvider>());
            Assert.AreEqual(Probe, restoredProtector.Unprotect(protectedProbe));
            return restored;
        }
        catch
        {
            restored.Dispose();
            throw;
        }
    }

    public void Dispose() => root.Delete(recursive: true);

    private static string CertificatePath(DirectoryInfo directory) => Path.Combine(directory.FullName, "active.pfx");

    private ServiceProvider Open(DirectoryInfo keys, string certificate)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing", DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(SettingsFor(keys.FullName, certificate));
        builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
        return builder.Services.BuildServiceProvider();
    }

    private Dictionary<string, string?> SettingsFor(string keys, string certificate) => new()
    {
        ["DataProtection:ApplicationName"] = ApplicationName,
        ["DataProtection:KeyRingPath"] = keys,
        ["DataProtection:CertificatePath"] = certificate,
        ["DataProtection:CertificatePassword"] = password
    };
}
