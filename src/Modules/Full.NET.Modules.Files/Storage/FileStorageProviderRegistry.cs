using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Storage;

/// <summary>Files 存储路由配置；默认 Provider 只能由受信任的宿主配置指定。</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "Files:Storage";

    /// <summary>新对象使用的默认 Provider 稳定机器码。</summary>
    public string DefaultProviderKey { get; set; } = LocalHostFileBlobStorage.Key;
}

/// <summary>按稳定机器码解析存储 Provider，并在重复或未知配置时拒绝启动。</summary>
internal sealed partial class FileStorageProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IFileStorageProvider> _providers;

    public FileStorageProviderRegistry(
        IEnumerable<IFileStorageProvider> providers,
        IOptions<FileStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);

        var providerMap = new Dictionary<string, IFileStorageProvider>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            EnsureCanonicalKey(provider.ProviderKey);
            if (!providerMap.TryAdd(provider.ProviderKey, provider))
            {
                throw new InvalidOperationException(
                    $"File storage provider key '{provider.ProviderKey}' is registered more than once.");
            }
        }

        if (providerMap.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one file storage provider must be registered.");
        }

        _providers = providerMap;
        DefaultProvider = Resolve(options.Value.DefaultProviderKey);
    }

    /// <summary>新上传对象使用的受信任默认 Provider。</summary>
    public IFileStorageProvider DefaultProvider { get; }

    /// <summary>按已持久化机器码解析 Provider；未知值必须失败，禁止回退到默认实现。</summary>
    public IFileStorageProvider Resolve(string providerKey)
    {
        EnsureCanonicalKey(providerKey);
        return _providers.TryGetValue(providerKey, out var provider)
            ? provider
            : throw new InvalidOperationException(
                $"File storage provider key '{providerKey}' is not registered.");
    }

    internal static bool IsCanonicalKey(string? providerKey) =>
        providerKey is not null && ProviderKeyPattern().IsMatch(providerKey);

    private static void EnsureCanonicalKey(string? providerKey)
    {
        if (!IsCanonicalKey(providerKey))
        {
            throw new InvalidOperationException(
                "File storage provider keys must be lowercase stable machine codes.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderKeyPattern();
}

/// <summary>在宿主启动时验证默认 Provider 语法和注册集合，避免首个文件请求才暴露配置错误。</summary>
internal sealed class FileStorageOptionsValidator(
    IEnumerable<IFileStorageProvider> providers,
    IHostEnvironment environment) : IValidateOptions<FileStorageOptions>
{
    public ValidateOptionsResult Validate(string? name, FileStorageOptions options)
    {
        if (!FileStorageProviderRegistry.IsCanonicalKey(options.DefaultProviderKey))
        {
            return ValidateOptionsResult.Fail(
                "Files:Storage:DefaultProviderKey must be a lowercase stable machine code.");
        }

        // 多实例生产禁止默认落本地磁盘；历史 local 对象仍可通过 Resolve("local") 读取。
        if (environment.IsProduction()
            && !string.Equals(
                options.DefaultProviderKey,
                S3HostFileBlobStorage.Key,
                StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Production Files:Storage:DefaultProviderKey must be 's3'.");
        }

        try
        {
            _ = new FileStorageProviderRegistry(
                providers,
                Options.Create(options));
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
