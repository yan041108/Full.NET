using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Full.NET.Hosting.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// 验证两套独立 DI 容器共享同一 Key Ring 与证书时可互相解保护。
/// </summary>
internal static class DataProtectionMultiInstanceAssertions
{
    public static async Task VerifyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.Combine(
            Path.GetTempPath(),
            "fullnet-dp-mi-" + Guid.NewGuid().ToString("N"));
        var keyRing = Path.Combine(root, "keys");
        var activePfx = Path.Combine(root, "active.pfx");
        var historicalPfx = Path.Combine(root, "historical.pfx");
        const string password = "FullNet-Test-Only!";
        Directory.CreateDirectory(root);
        try
        {
            CreateSelfSignedPfx(activePfx, password, "CN=Full.NET.DP.Active");
            CreateSelfSignedPfx(historicalPfx, password, "CN=Full.NET.DP.Historical");

            await using var providerA = BuildProvider(
                keyRing,
                activePfx,
                password,
                historicalPfx);
            await using var providerB = BuildProvider(
                keyRing,
                activePfx,
                password,
                historicalPfx);

            var protectorA = providerA
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Full.NET.Tests.DataProtection");
            var protectorB = providerB
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Full.NET.Tests.DataProtection");

            var payload = "tenant-secret-" + Guid.NewGuid().ToString("N");
            var protectedPayload = protectorA.Protect(payload);
            Assert.AreEqual(payload, protectorB.Unprotect(protectedPayload));

            await using var writerHistorical = BuildProvider(
                keyRing,
                historicalPfx,
                password,
                historicalPaths: []);
            var historicalProtector = writerHistorical
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Full.NET.Tests.DataProtection.History");
            var historicalProtected = historicalProtector.Protect("legacy-" + payload);

            await using var readerWithHistory = BuildProvider(
                keyRing,
                activePfx,
                password,
                historicalPfx);
            var reader = readerWithHistory
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Full.NET.Tests.DataProtection.History");
            Assert.AreEqual("legacy-" + payload, reader.Unprotect(historicalProtected));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 测试清理尽力而为。
            }
        }
    }

    private static ServiceProvider BuildProvider(
        string keyRingPath,
        string certificatePath,
        string password,
        string? historicalPath = null,
        string[]? historicalPaths = null)
    {
        var environment = new StubHostEnvironment(
            Environments.Staging,
            Path.GetDirectoryName(keyRingPath)!);

        var values = new Dictionary<string, string?>
        {
            ["DataProtection:ApplicationName"] = "Full.NET.MultiInstance",
            ["DataProtection:KeyRingPath"] = keyRingPath,
            ["DataProtection:CertificatePath"] = certificatePath,
            ["DataProtection:CertificatePassword"] = password,
        };
        var history = historicalPaths
            ?? (historicalPath is null ? [] : new[] { historicalPath });
        for (var i = 0; i < history.Length; i++)
        {
            values[$"DataProtection:HistoricalCertificatePaths:{i}"] = history[i];
            values[$"DataProtection:HistoricalCertificatePasswords:{i}"] = password;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(environment);
        services.AddFullNetDataProtection(configuration, environment);
        return services.BuildServiceProvider();
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
        var bytes = certificate.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(path, bytes);
    }

    private sealed class StubHostEnvironment(string environmentName, string contentRootPath)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Full.NET.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }
}

[TestClass]
public sealed class DataProtectionMultiInstanceTests
{
    [TestMethod]
    public Task Shared_key_ring_protects_and_unprotects_across_instances() =>
        DataProtectionMultiInstanceAssertions.VerifyAsync();
}
