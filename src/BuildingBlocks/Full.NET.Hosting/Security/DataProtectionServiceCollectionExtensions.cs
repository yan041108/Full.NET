using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Security;

public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// 为 API/Worker 注册共享 Data Protection Key Ring。Production 强制文件系统持久化与证书保护。
    /// </summary>
    public static IServiceCollection AddFullNetDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        // BindConfiguration 在解析 IOptions 时需要 IConfiguration 已注册到 DI。
        services.TryAddSingleton(configuration);

        services.AddOptions<DataProtectionOptions>()
            .BindConfiguration(DataProtectionOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<DataProtectionOptions>,
            DataProtectionOptionsValidator>());

        // ValidateOnStart 在构建宿主时触发；此处先读取一次以在注册阶段 fail-fast。
        var options = configuration
                .GetSection(DataProtectionOptions.SectionName)
                .Get<DataProtectionOptions>()
            ?? new DataProtectionOptions();
        var validation = new DataProtectionOptionsValidator(environment)
            .Validate(name: null, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                DataProtectionOptions.SectionName,
                typeof(DataProtectionOptions),
                validation.Failures!);
        }

        var keyRingPath = ResolveKeyRingPath(options, environment);
        var builder = services
            .AddDataProtection()
            .SetApplicationName(options.ApplicationName.Trim());

        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            Directory.CreateDirectory(keyRingPath);
            builder.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Production DataProtection KeyRingPath resolved empty.");
        }

        if (!string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            var active = LoadCertificate(
                options.CertificatePath,
                options.CertificatePassword,
                requirePrivateKey: true);
            builder.ProtectKeysWithCertificate(active);

            var historical = LoadHistoricalCertificates(options).ToArray();
            EnsureDistinctCertificates(active, historical);
            if (historical.Length > 0)
            {
                builder.UnprotectKeysWithAnyCertificate(historical);
            }
        }
        else if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Production DataProtection requires CertificatePath.");
        }

        return services;
    }

    private static string? ResolveKeyRingPath(
        DataProtectionOptions options,
        IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(options.KeyRingPath))
        {
            if (environment.IsProduction())
            {
                return null;
            }

            // 开发默认落到 ContentRoot，避免各实例各自 ephemeral 密钥导致无法互解。
            return Path.GetFullPath(
                Path.Combine(environment.ContentRootPath, "App_Data", "data-protection-keys"));
        }

        var path = options.KeyRingPath;
        if (path.StartsWith("~/", StringComparison.Ordinal)
            || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            path = Path.Combine(environment.ContentRootPath, path[2..]);
        }
        else if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(environment.ContentRootPath, path);
        }

        return Path.GetFullPath(path);
    }

    private static IEnumerable<X509Certificate2> LoadHistoricalCertificates(
        DataProtectionOptions options)
    {
        for (var i = 0; i < options.HistoricalCertificatePaths.Length; i++)
        {
            var path = options.HistoricalCertificatePaths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var password = options.HistoricalCertificatePasswords.Length > i
                ? options.HistoricalCertificatePasswords[i]
                : options.CertificatePassword;
            yield return LoadCertificate(path, password, requirePrivateKey: true);
        }
    }

    private static X509Certificate2 LoadCertificate(
        string path,
        string? password,
        bool requirePrivateKey)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"DataProtection certificate file was not found: {path}",
                path);
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
        if (requirePrivateKey && !certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"DataProtection certificate '{path}' must include a private key.");
        }

        return certificate;
    }

    private static void EnsureDistinctCertificates(
        X509Certificate2 active,
        IReadOnlyList<X509Certificate2> historical)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            active.Thumbprint,
        };
        foreach (var certificate in historical)
        {
            if (!seen.Add(certificate.Thumbprint))
            {
                throw new InvalidOperationException(
                    "DataProtection certificate thumbprints must be unique across active and historical entries.");
            }
        }
    }
}
