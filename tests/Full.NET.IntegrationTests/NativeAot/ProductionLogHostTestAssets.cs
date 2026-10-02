using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>为生产环境原生日志测试提供真实密钥配置，不放宽宿主的生产安全校验。</summary>
internal sealed class ProductionLogHostTestAssets : IDisposable
{
    private readonly string _root;
    private readonly string _password = Guid.NewGuid().ToString("N");
    private readonly RSA _signingKey = RSA.Create(3072);

    public ProductionLogHostTestAssets()
    {
        // 使用隔离的产物目录；生产 Key Ring 禁止落入系统临时目录。
        _root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
            "artifacts", "native-aot", "production-log-assets", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Path.Combine(_root, "keys"));
        using var certificateKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=Full.NET.ProductionLogTest", certificateKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(Path.Combine(_root, "protection.pfx"),
            certificate.Export(X509ContentType.Pfx, _password));
    }

    public void ApplyTo(IDictionary<string, string?> settings)
    {
        settings["DOTNET_ENVIRONMENT"] = "Production";
        settings["ASPNETCORE_ENVIRONMENT"] = "Production";
        settings["DataProtection:KeyRingPath"] = Path.Combine(_root, "keys");
        settings["DataProtection:CertificatePath"] = Path.Combine(_root, "protection.pfx");
        settings["DataProtection:CertificatePassword"] = _password;
        settings["Identity:AllowDevelopmentEphemeralSigningKey"] = "false";
        settings["Identity:ActiveKeyId"] = "production-log-test";
        settings["Identity:SigningKeys:production-log-test:PrivateKeyPem"] = _signingKey.ExportRSAPrivateKeyPem();
        settings["Identity:SigningKeys:production-log-test:PublicKeyPem"] = _signingKey.ExportRSAPublicKeyPem();
        settings["Identity:EnableRemoteSuperAdministratorManagement"] = "false";
        settings["Identity:RequireSecureCookies"] = "true";
        settings["Realtime:Enabled"] = "false";
        settings["Realtime:AllowSharedRedisInDevelopment"] = "false";
        // 未调用的云文件 Provider 也有生产配置校验；仅提供本地测试占位，不产生外部请求。
        settings["Files:Storage:DefaultProviderKey"] = "s3";
        settings["Files:S3:BucketName"] = "native-log-test";
        settings["Files:S3:Region"] = "us-east-1";
        settings["Files:S3:AccessKeyId"] = "native-log-test";
        settings["Files:S3:SecretAccessKey"] = Guid.NewGuid().ToString("N");
        settings["Files:Oss:BucketName"] = "native-log-test";
        settings["Files:Oss:Endpoint"] = "https://oss.example.invalid";
        settings["Files:Oss:AccessKeyId"] = "native-log-test";
        settings["Files:Oss:AccessKeySecret"] = Guid.NewGuid().ToString("N");
    }

    public void Dispose()
    {
        _signingKey.Dispose();
        // 根目录为本实例创建的独立 GUID 目录，仅清理本次证书与 Key Ring。
        Directory.Delete(_root, recursive: true);
    }
}
