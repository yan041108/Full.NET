using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Storage;

/// <summary>S3 对象操作窄接口，隔离 AWSSDK 以便单测与共享替身。</summary>
internal interface IS3BlobClient
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
}

/// <summary>基于 AWSSDK.S3 的窄接口实现。</summary>
internal sealed class AmazonS3BlobClient : IS3BlobClient, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly bool _ownsClient;

    public AmazonS3BlobClient(IAmazonS3 client, bool ownsClient)
    {
        _client = client;
        _ownsClient = ownsClient;
    }

    public static AmazonS3BlobClient Create(S3FileStorageOptions options)
    {
        var config = new AmazonS3Config
        {
            Timeout = options.RequestTimeout,
            MaxErrorRetry = 2,
            ForcePathStyle = options.ForcePathStyle,
        };

        if (options.EndpointMode == S3EndpointMode.Custom)
        {
            config.ServiceURL = options.ServiceUrl;
            config.AuthenticationRegion = options.Region;
            config.ForcePathStyle = true;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        var credentials = S3HostFileBlobStorage.ResolveCredentials();
        var client = credentials is null
            ? new AmazonS3Client(config)
            : new AmazonS3Client(credentials, config);
        return new AmazonS3BlobClient(client, ownsClient: true);
    }

    public async Task PutAsync(
        string bucketName,
        string objectKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        var put = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = content,
            AutoCloseStream = false,
        };
        await _client.PutObjectAsync(put, cancellationToken).ConfigureAwait(false);
    }

    public Task CopyAsync(
        string bucketName,
        string sourceKey,
        string destinationKey,
        CancellationToken cancellationToken) =>
        _client.CopyObjectAsync(
            new CopyObjectRequest
            {
                SourceBucket = bucketName,
                SourceKey = sourceKey,
                DestinationBucket = bucketName,
                DestinationKey = destinationKey,
            },
            cancellationToken);

    public Task DeleteAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken) =>
        _client.DeleteObjectAsync(bucketName, objectKey, cancellationToken);

    public async Task<bool> ExistsAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetObjectMetadataAsync(bucketName, objectKey, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            return false;
        }
    }

    public async Task<Stream> OpenReadAsync(
        string bucketName,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectAsync(bucketName, objectKey, cancellationToken)
                .ConfigureAwait(false);
            return new S3ObjectReadStream(response);
        }
        catch (AmazonS3Exception exception) when (IsNotFound(exception))
        {
            throw new FileNotFoundException("Stored blob was not found.", objectKey, exception);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    internal static bool IsNotFound(AmazonS3Exception exception) =>
        exception.StatusCode == System.Net.HttpStatusCode.NotFound
        || string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
        || string.Equals(exception.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase);

    private sealed class S3ObjectReadStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
        private bool _disposed;

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _inner.Dispose();
                response.Dispose();
                _disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// S3 兼容对象存储。对象键只接受模块生成的相对键；Save 经临时对象发布，避免暴露部分最终对象。
/// </summary>
internal sealed class S3HostFileBlobStorage : IFileStorageProvider, IDisposable
{
    public const string Key = "s3";

    private readonly IOptionsMonitor<S3FileStorageOptions> _options;
    private readonly IS3BlobClient? _injectedClient;
    private AmazonS3BlobClient? _ownedClient;
    private readonly object _clientGate = new();

    public S3HostFileBlobStorage(IOptionsMonitor<S3FileStorageOptions> options)
        : this(options, client: null)
    {
    }

    // 测试可注入窄接口；生产路径懒创建真实客户端，避免未配置 S3 时启动失败。
    internal S3HostFileBlobStorage(
        IOptionsMonitor<S3FileStorageOptions> options,
        IS3BlobClient? client)
    {
        _options = options;
        _injectedClient = client;
    }

    public string ProviderKey => Key;

    private IS3BlobClient Client
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
                return _ownedClient ??= AmazonS3BlobClient.Create(_options.CurrentValue);
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
            throw new IOException($"S3 object '{objectKey}' already exists.");
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
        // S3 DeleteObject 对缺失键幂等成功。
        await Client.DeleteAsync(options.BucketName, objectKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        _ownedClient?.Dispose();
        _ownedClient = null;
    }

    internal static AWSCredentials? ResolveCredentials()
    {
        var accessKey = Environment.GetEnvironmentVariable(
                S3FileStorageOptions.AccessKeyEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable(
                S3FileStorageOptions.SecretKeyEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            // 回退到 SDK 默认链（实例角色 / Web Identity 等）。
            return null;
        }

        var session = Environment.GetEnvironmentVariable(
                S3FileStorageOptions.SessionTokenEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN");
        return string.IsNullOrWhiteSpace(session)
            ? new BasicAWSCredentials(accessKey, secretKey)
            : new SessionAWSCredentials(accessKey, secretKey, session);
    }

    private static void EnsureBucketConfigured(S3FileStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new InvalidOperationException(
                $"{S3FileStorageOptions.SectionName}:BucketName is not configured.");
        }
    }

    private static string NormalizeObjectKey(string storageKey)
    {
        ArgumentNullException.ThrowIfNull(storageKey);
        // 与 Local Provider 对齐：非法相对键统一抛 InvalidOperationException，便于调用方按契约处理。
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
