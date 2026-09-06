using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Storage;

/// <summary>OSS 对象操作窄接口，隔离 HTTP 签名实现以便单测与共享替身。</summary>
internal interface IOssBlobClient
{
    Task PutAsync(
        string bucketName,
        string objectKey,
        Stream content,
        CancellationToken cancellationToken);

    Task CopyAsync(
        string bucketName,
        string sourceKey,
        string destinationKey,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken);

    /// <summary>探测 Bucket 与凭据是否可用；对象不存在时仍视为连通成功。</summary>
    Task<OssConnectivityProbeResult> ProbeConnectivityAsync(
        string bucketName,
        CancellationToken cancellationToken);
}

/// <summary>OSS 连通性探测结果。</summary>
internal readonly record struct OssConnectivityProbeResult(bool Succeeded, string Message);

/// <summary>基于 OSS REST API V1 签名的窄接口实现。</summary>
internal sealed class HttpOssBlobClient : IOssBlobClient
{
    public const string HttpClientName = "Files.Oss";
    public const string ConnectivityProbeObjectKey = ".fullnet-storage-connectivity-probe";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OssFileStorageOptions _options;
    private readonly string _accessKeyId;
    private readonly string _accessKeySecret;

    public HttpOssBlobClient(
        IHttpClientFactory httpClientFactory,
        OssFileStorageOptions options,
        string accessKeyId,
        string accessKeySecret)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _accessKeyId = accessKeyId;
        _accessKeySecret = accessKeySecret;
    }

    public static HttpOssBlobClient Create(
        IHttpClientFactory httpClientFactory,
        OssFileStorageOptions options)
    {
        var (accessKeyId, accessKeySecret) = OssHostFileBlobStorage.ResolveCredentials();
        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(accessKeySecret))
        {
            throw new InvalidOperationException(
                $"{OssFileStorageOptions.SectionName}:credentials are not configured.");
        }

        return new HttpOssBlobClient(httpClientFactory, options, accessKeyId, accessKeySecret);
    }

    public async Task PutAsync(
        string bucketName,
        string objectKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(
            HttpMethod.Put,
            bucketName,
            objectKey,
            contentType: "application/octet-stream");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task CopyAsync(
        string bucketName,
        string sourceKey,
        string destinationKey,
        CancellationToken cancellationToken)
    {
        var copySource = $"/{bucketName}/{EncodeObjectKey(sourceKey)}";
        using var request = BuildRequest(HttpMethod.Put, bucketName, destinationKey);
        request.Headers.TryAddWithoutValidation("x-oss-copy-source", copySource);
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Delete, bucketName, objectKey);
        await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Head, bucketName, objectKey);
        using var response = await SendWithResponseAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowOssFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<Stream> OpenReadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Get, bucketName, objectKey);
        var response = await SendWithResponseAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            throw new FileNotFoundException("Stored blob was not found.", objectKey);
        }

        if (!response.IsSuccessStatusCode)
        {
            await ThrowOssFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OssConnectivityProbeResult> ProbeConnectivityAsync(
        string bucketName,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = BuildRequest(
                HttpMethod.Head,
                bucketName,
                ConnectivityProbeObjectKey);
            using var response = await SendWithResponseAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            {
                return new OssConnectivityProbeResult(true, "OSS bucket is reachable.");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new OssConnectivityProbeResult(
                    false,
                    "OSS rejected the request; verify credentials and bucket policy.");
            }

            return new OssConnectivityProbeResult(
                false,
                $"OSS returned HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException exception)
        {
            return new OssConnectivityProbeResult(
                false,
                $"OSS endpoint could not be reached: {exception.Message}");
        }
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method,
        string bucketName,
        string objectKey,
        string? contentType = null)
    {
        var requestUri = BuildRequestUri(bucketName, objectKey);
        var request = new HttpRequestMessage(method, requestUri);
        var date = DateTime.UtcNow;
        request.Headers.TryAddWithoutValidation("Date", date.ToString("R", CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(contentType))
        {
            request.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        var authorization = BuildAuthorization(
            method.Method,
            string.Empty,
            contentType ?? string.Empty,
            date,
            request.Headers,
            BuildCanonicalizedResource(bucketName, objectKey));
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return request;
    }

    private async Task SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithResponseAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowOssFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendWithResponseAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = _options.RequestTimeout;
        return await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ThrowOssFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException(
            $"OSS request failed with HTTP {(int)response.StatusCode}: {body}");
    }

    private Uri BuildRequestUri(string bucketName, string objectKey)
    {
        var endpoint = NormalizeEndpointHost(_options.Endpoint);
        var scheme = _options.AllowInsecureEndpoint ? "http" : "https";
        var encodedKey = EncodeObjectKey(objectKey);
        return new Uri($"{scheme}://{bucketName}.{endpoint}/{encodedKey}");
    }

    private static string NormalizeEndpointHost(string endpoint)
    {
        var trimmed = endpoint.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(trimmed).Host;
        }

        return trimmed.TrimEnd('/');
    }

    private static string BuildCanonicalizedResource(string bucketName, string objectKey) =>
        $"/{bucketName}/{objectKey}";

    private static string EncodeObjectKey(string objectKey) =>
        string.Join("/", objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    private string BuildAuthorization(
        string method,
        string contentMd5,
        string contentType,
        DateTime date,
        System.Net.Http.Headers.HttpRequestHeaders headers,
        string canonicalizedResource)
    {
        var ossHeaders = headers
            .Where(header => header.Key.StartsWith("x-oss-", StringComparison.OrdinalIgnoreCase))
            .SelectMany(header => header.Value.Select(value => (header.Key.ToLowerInvariant(), value ?? string.Empty)))
            .OrderBy(header => header.Item1, StringComparer.Ordinal)
            .ToArray();
        var canonicalizedOssHeaders = ossHeaders.Length == 0
            ? string.Empty
            : string.Join(
                    "\n",
                    ossHeaders.Select(header => $"{header.Item1}:{header.Item2}"))
                + "\n";
        var stringToSign = string.Join(
            "\n",
            method,
            contentMd5,
            contentType,
            date.ToString("R", CultureInfo.InvariantCulture),
            $"{canonicalizedOssHeaders}{canonicalizedResource}");
        var signature = Convert.ToBase64String(
            HMACSHA1.HashData(
                Encoding.UTF8.GetBytes(_accessKeySecret),
                Encoding.UTF8.GetBytes(stringToSign)));
        return $"OSS {_accessKeyId}:{signature}";
    }
}

/// <summary>
/// 阿里云 OSS 对象存储。对象键只接受模块生成的相对键；Save 经临时对象发布，避免暴露部分最终对象。
/// </summary>
internal sealed class OssHostFileBlobStorage : IFileStorageProvider, IDisposable
{
    public const string Key = "oss";

    private readonly IOptionsMonitor<OssFileStorageOptions> _options;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOssBlobClient? _injectedClient;
    private HttpOssBlobClient? _ownedClient;
    private readonly object _clientGate = new();

    public OssHostFileBlobStorage(
        IOptionsMonitor<OssFileStorageOptions> options,
        IHttpClientFactory httpClientFactory)
        : this(options, httpClientFactory, client: null)
    {
    }

    internal OssHostFileBlobStorage(
        IOptionsMonitor<OssFileStorageOptions> options,
        IHttpClientFactory? httpClientFactory,
        IOssBlobClient? client)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _injectedClient = client;
    }

    public string ProviderKey => Key;

    private IOssBlobClient Client
    {
        get
        {
            if (_injectedClient is not null)
            {
                return _injectedClient;
            }

            if (_ownedClient is not null)
            {
                return _ownedClient;
            }

            lock (_clientGate)
            {
                return _ownedClient ??= HttpOssBlobClient.Create(
                    _httpClientFactory
                        ?? throw new InvalidOperationException("HTTP client factory is not configured."),
                    _options.CurrentValue);
            }
        }
    }

    public async Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        var objectKey = NormalizeObjectKey(storageKey);
        var options = _options.CurrentValue;
        EnsureBucketConfigured(options);

        if (await Client.ExistsAsync(options.BucketName, objectKey, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new IOException($"OSS object '{objectKey}' already exists.");
        }

        var stagingKey = objectKey + $".{Guid.NewGuid():N}.uploading";
        try
        {
            await Client.PutAsync(options.BucketName, stagingKey, content, cancellationToken)
                .ConfigureAwait(false);
            await Client.CopyAsync(options.BucketName, stagingKey, objectKey, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await Client.DeleteAsync(options.BucketName, stagingKey, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 清理失败不掩盖主异常；残留 staging 可由运维生命周期规则回收。
            }
        }
    }

    public async Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var objectKey = NormalizeObjectKey(storageKey);
        var options = _options.CurrentValue;
        EnsureBucketConfigured(options);
        return await Client.OpenReadAsync(options.BucketName, objectKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var objectKey = NormalizeObjectKey(storageKey);
        var options = _options.CurrentValue;
        EnsureBucketConfigured(options);
        return await Client.ExistsAsync(options.BucketName, objectKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        var objectKey = NormalizeObjectKey(storageKey);
        var options = _options.CurrentValue;
        EnsureBucketConfigured(options);
        await Client.DeleteAsync(options.BucketName, objectKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        _ownedClient = null;
    }

    internal static (string? AccessKeyId, string? AccessKeySecret) ResolveCredentials()
    {
        var accessKeyId = Environment.GetEnvironmentVariable(
            OssFileStorageOptions.AccessKeyIdEnvironmentVariable);
        var accessKeySecret = Environment.GetEnvironmentVariable(
            OssFileStorageOptions.AccessKeySecretEnvironmentVariable);
        return (
            string.IsNullOrWhiteSpace(accessKeyId) ? null : accessKeyId,
            string.IsNullOrWhiteSpace(accessKeySecret) ? null : accessKeySecret);
    }

    private static void EnsureBucketConfigured(OssFileStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new InvalidOperationException(
                $"{OssFileStorageOptions.SectionName}:BucketName is not configured.");
        }
    }

    private static string NormalizeObjectKey(string storageKey)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        var normalizedKey = storageKey.Replace('\\', '/').Trim('/');
        if (normalizedKey.Length == 0
            || normalizedKey.Contains("..", StringComparison.Ordinal)
            || storageKey.StartsWith('/')
            || storageKey.StartsWith('\\')
            || Path.IsPathRooted(normalizedKey)
            || Path.IsPathRooted(storageKey))
        {
            throw new InvalidOperationException("Storage key is invalid.");
        }

        return normalizedKey;
    }
}
