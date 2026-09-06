using Amazon.S3;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Features.ManageStorageProviders;

/// <summary>汇总已注册存储 Provider 的展示信息与受界连通性测试。</summary>
internal sealed class FileStorageProviderCatalogService
{
    private const string ConnectivityProbeObjectKey = ".fullnet-storage-connectivity-probe";

    private readonly IEnumerable<IFileStorageProvider> _providers;
    private readonly FileStorageProviderRegistry _registry;
    private readonly IOptionsMonitor<LocalFileStorageOptions> _localOptions;
    private readonly IOptionsMonitor<S3FileStorageOptions> _s3Options;
    private readonly IOptionsMonitor<OssFileStorageOptions> _ossOptions;
    private readonly IHttpClientFactory _httpClientFactory;

    public FileStorageProviderCatalogService(
        IEnumerable<IFileStorageProvider> providers,
        FileStorageProviderRegistry registry,
        IOptionsMonitor<LocalFileStorageOptions> localOptions,
        IOptionsMonitor<S3FileStorageOptions> s3Options,
        IOptionsMonitor<OssFileStorageOptions> ossOptions,
        IHttpClientFactory httpClientFactory)
    {
        _providers = providers;
        _registry = registry;
        _localOptions = localOptions;
        _s3Options = s3Options;
        _ossOptions = ossOptions;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>列出所有已注册 Provider 的目录信息。</summary>
    public IReadOnlyList<StorageProviderCatalogItem> List()
    {
        var defaultKey = _registry.DefaultProvider.ProviderKey;
        return _providers
            .Select(provider => BuildCatalogItem(provider.ProviderKey, defaultKey))
            .OrderBy(item => item.ProviderKey, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>对指定 Provider 执行受界连通性测试。</summary>
    public async Task<Result<TestStorageProviderConnectivityResult>> TestConnectivityAsync(
        string providerKey,
        CancellationToken cancellationToken)
    {
        if (!FileStorageProviderRegistry.IsCanonicalKey(providerKey))
        {
            return Result<TestStorageProviderConnectivityResult>.Failure(new Error(
                FilesErrorCodes.StorageProviderNotFound,
                "Storage provider key is invalid.",
                ErrorType.NotFound));
        }

        try
        {
            _ = _registry.Resolve(providerKey);
        }
        catch (InvalidOperationException)
        {
            return Result<TestStorageProviderConnectivityResult>.Failure(new Error(
                FilesErrorCodes.StorageProviderNotFound,
                "Storage provider is not registered.",
                ErrorType.NotFound));
        }

        if (string.Equals(providerKey, LocalHostFileBlobStorage.Key, StringComparison.Ordinal))
        {
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(
                    false,
                    "Local disk storage does not support remote connectivity tests."));
        }

        if (string.Equals(providerKey, S3HostFileBlobStorage.Key, StringComparison.Ordinal))
        {
            return await TestS3ConnectivityAsync(cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(providerKey, OssHostFileBlobStorage.Key, StringComparison.Ordinal))
        {
            return await TestOssConnectivityAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<TestStorageProviderConnectivityResult>.Failure(new Error(
            FilesErrorCodes.StorageProviderNotFound,
            "Storage provider connectivity test is not implemented.",
            ErrorType.NotFound));
    }

    private StorageProviderCatalogItem BuildCatalogItem(string providerKey, string defaultKey)
    {
        if (string.Equals(providerKey, LocalHostFileBlobStorage.Key, StringComparison.Ordinal))
        {
            var local = _localOptions.CurrentValue;
            var configured = !string.IsNullOrWhiteSpace(local.RootPath);
            return new StorageProviderCatalogItem(
                providerKey,
                "本地磁盘",
                "local",
                string.Equals(defaultKey, providerKey, StringComparison.Ordinal),
                configured,
                configured ? SummarizeLocalPath(local.RootPath) : null,
                false);
        }

        if (string.Equals(providerKey, S3HostFileBlobStorage.Key, StringComparison.Ordinal))
        {
            var s3 = _s3Options.CurrentValue;
            var configured = IsS3Configured(s3);
            return new StorageProviderCatalogItem(
                providerKey,
                "S3 兼容对象存储",
                "s3",
                string.Equals(defaultKey, providerKey, StringComparison.Ordinal),
                configured,
                configured ? SummarizeS3(s3) : null,
                true);
        }

        if (string.Equals(providerKey, OssHostFileBlobStorage.Key, StringComparison.Ordinal))
        {
            var oss = _ossOptions.CurrentValue;
            var configured = IsOssConfigured(oss);
            return new StorageProviderCatalogItem(
                providerKey,
                "阿里云 OSS",
                "oss",
                string.Equals(defaultKey, providerKey, StringComparison.Ordinal),
                configured,
                configured ? SummarizeOss(oss) : null,
                true);
        }

        return new StorageProviderCatalogItem(
            providerKey,
            providerKey,
            "unknown",
            string.Equals(defaultKey, providerKey, StringComparison.Ordinal),
            false,
            null,
            false);
    }

    private async Task<Result<TestStorageProviderConnectivityResult>> TestS3ConnectivityAsync(
        CancellationToken cancellationToken)
    {
        var options = _s3Options.CurrentValue;
        if (!IsS3Configured(options))
        {
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(
                    false,
                    "S3 storage is not fully configured."));
        }

        try
        {
            using var client = AmazonS3BlobClient.Create(options);
            _ = await client.ExistsAsync(
                    options.BucketName,
                    ConnectivityProbeObjectKey,
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(
                    true,
                    "S3 bucket is reachable."));
        }
        catch (AmazonS3Exception exception) when (IsS3AuthFailure(exception))
        {
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(
                    false,
                    "S3 rejected the request; verify credentials and bucket policy."));
        }
        catch (Exception exception)
        {
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(
                    false,
                    $"S3 endpoint could not be reached: {exception.Message}"));
        }
    }

    private async Task<Result<TestStorageProviderConnectivityResult>> TestOssConnectivityAsync(
        CancellationToken cancellationToken)
    {
        var options = _ossOptions.CurrentValue;
        if (!IsOssConfigured(options))
        {
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(
                    false,
                    "OSS storage is not fully configured."));
        }

        try
        {
            var client = HttpOssBlobClient.Create(_httpClientFactory, options);
            var probe = await client.ProbeConnectivityAsync(options.BucketName, cancellationToken)
                .ConfigureAwait(false);
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(probe.Succeeded, probe.Message));
        }
        catch (Exception exception)
        {
            return Result<TestStorageProviderConnectivityResult>.Success(
                new TestStorageProviderConnectivityResult(
                    false,
                    $"OSS endpoint could not be reached: {exception.Message}"));
        }
    }

    private static bool IsS3Configured(S3FileStorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.BucketName)
        && (options.EndpointMode == S3EndpointMode.Aws
            ? !string.IsNullOrWhiteSpace(options.Region)
            : !string.IsNullOrWhiteSpace(options.ServiceUrl)
                && !string.IsNullOrWhiteSpace(options.Region));

    private static bool IsOssConfigured(OssFileStorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.BucketName)
        && !string.IsNullOrWhiteSpace(options.Endpoint)
        && OssHostFileBlobStorage.ResolveCredentials() is ({ } accessKeyId, { } accessKeySecret)
        && !string.IsNullOrWhiteSpace(accessKeyId)
        && !string.IsNullOrWhiteSpace(accessKeySecret);

    private static string SummarizeLocalPath(string rootPath)
    {
        var normalized = rootPath.Replace('\\', '/').TrimEnd('/');
        if (normalized.Length <= 48)
        {
            return $"root={normalized}";
        }

        return $"root={normalized[..24]}…{normalized[^16..]}";
    }

    private static string SummarizeS3(S3FileStorageOptions options)
    {
        var endpoint = options.EndpointMode == S3EndpointMode.Custom
            ? RedactEndpoint(options.ServiceUrl)
            : $"region={options.Region}";
        return $"bucket={options.BucketName}; {endpoint}";
    }

    private static string SummarizeOss(OssFileStorageOptions options) =>
        $"bucket={options.BucketName}; endpoint={RedactEndpoint(options.Endpoint)}";

    private static string? RedactEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absolute))
        {
            return absolute.Host;
        }

        return endpoint.Trim();
    }

    private static bool IsS3AuthFailure(AmazonS3Exception exception) =>
        exception.StatusCode == System.Net.HttpStatusCode.Forbidden
        || string.Equals(exception.ErrorCode, "InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase)
        || string.Equals(exception.ErrorCode, "SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase);
}
