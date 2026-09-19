using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Hosting.Security;

/// <summary>
/// Full.NET Data Protection 共享 Key Ring 与证书保护的依赖注入扩展方法集合。
/// </summary>
/// <remarks>
/// Production 环境强制 Key Ring 文件持久化与 X509 证书保护；Development 默认落到 ContentRoot 下的 App_Data 目录以避免各实例 ephemeral 密钥互不可解。该扩展在注册阶段即对 DataProtectionOptions 做一次 fail-fast 校验，避免运行时才发现配置错误。
/// </remarks>
public static class DataProtectionServiceCollectionExtensions
{
    /// <summary>
    /// 为 API/Worker 注册共享 Data Protection Key Ring。Production 强制文件系统持久化与证书保护。
    /// </summary>
    /// <param name="services">宿主服务集合。</param>
    /// <param name="configuration">应用配置根，包含 DataProtection 配置节与证书路径。</param>
    /// <param name="environment">宿主环境信息，用于判定 Production 与解析 ContentRoot。</param>
    /// <returns>原服务集合，便于链式装配。</returns>
    /// <exception cref="OptionsValidationException">DataProtectionOptions 配置项非法（如 Production 缺失 KeyRingPath 或 CertificatePath）。</exception>
    /// <exception cref="InvalidOperationException">Production 环境下 KeyRingPath 或 CertificatePath 解析为空，或证书缺少私钥，或证书指纹在 active/historical 集合中重复。</exception>
    /// <exception cref="FileNotFoundException">配置的证书文件不存在。</exception>
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
