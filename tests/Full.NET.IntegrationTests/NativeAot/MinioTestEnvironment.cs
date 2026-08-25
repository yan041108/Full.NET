using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 为 Native S3 E2E 启动真实 MinIO 容器；桶创建由测试基础设施完成，Native Host.Api 执行对象读写。
/// </summary>
internal sealed class MinioTestEnvironment : IAsyncDisposable
{
    private const string MinioImage = "minio/minio:RELEASE.2024-12-18T13-15-44Z";
    private const string RootUser = "minioadmin";
    private const string RootPassword = "minioadmin";
    private const ushort ApiPort = 9000;

    private readonly IContainer _container;
    private readonly string _bucketName;

    private MinioTestEnvironment(IContainer container, string bucketName, Uri serviceUri)
    {
        _container = container;
        _bucketName = bucketName;
        ServiceUri = serviceUri;
    }

    public Uri ServiceUri { get; }

    public string BucketName => _bucketName;

    public string Region => "us-east-1";

    public string AccessKeyId => RootUser;

    public string SecretAccessKey => RootPassword;

    public static async Task<MinioTestEnvironment> StartAsync()
    {
        var container = new ContainerBuilder(MinioImage)
            .WithCommand("server", "/data", "--console-address", ":9001")
            .WithEnvironment("MINIO_ROOT_USER", RootUser)
            .WithEnvironment("MINIO_ROOT_PASSWORD", RootPassword)
            .WithPortBinding(ApiPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(request => request
                    .ForPort(ApiPort)
                    .ForPath("/minio/health/ready")))
            .Build();
        await container.StartAsync().ConfigureAwait(false);

        var hostPort = container.GetMappedPublicPort(ApiPort);
        var dockerHost = Environment.GetEnvironmentVariable(
            "TESTCONTAINERS_HOST_OVERRIDE");
        var serviceHost = string.IsNullOrWhiteSpace(dockerHost)
            ? "127.0.0.1"
            : dockerHost.Trim();
        var serviceUri = new UriBuilder("http", serviceHost, hostPort).Uri;
        var bucketName = $"fullnet-native-aot-{Guid.NewGuid():N}".ToLowerInvariant();
        var environment = new MinioTestEnvironment(container, bucketName, serviceUri);
        await environment.EnsureBucketAsync().ConfigureAwait(false);
        return environment;
    }

    public IReadOnlyDictionary<string, string?> CreateNativeHostSettings()
    {
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Files:Storage:DefaultProviderKey"] = "s3",
            ["Files:S3:EndpointMode"] = "Custom",
            ["Files:S3:ServiceUrl"] = ServiceUri.ToString(),
            ["Files:S3:Region"] = Region,
            ["Files:S3:ForcePathStyle"] = "true",
            ["Files:S3:BucketName"] = BucketName,
            ["Files:S3:AllowInsecureServiceUrl"] = "true",
            ["Files:S3:AccessKeyId"] = AccessKeyId,
            ["Files:S3:SecretAccessKey"] = SecretAccessKey,
        };
    }

    public async Task<bool> ObjectExistsAsync(string objectKey)
    {
        using var client = CreateAdminClient();
        try
        {
            await client.GetObjectMetadataAsync(BucketName, objectKey).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await PurgeBucketAsync().ConfigureAwait(false);
        }
        catch
        {
            // 清理失败不得泄露凭据；容器销毁仍会继续。
        }

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private async Task EnsureBucketAsync()
    {
        using var client = CreateAdminClient();
        await client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = BucketName,
        }).ConfigureAwait(false);
    }

    private async Task PurgeBucketAsync()
    {
        using var client = CreateAdminClient();
        var list = await client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = BucketName,
        }).ConfigureAwait(false);
        foreach (var item in list.S3Objects)
        {
            await client.DeleteObjectAsync(BucketName, item.Key).ConfigureAwait(false);
        }

        await client.DeleteBucketAsync(BucketName).ConfigureAwait(false);
    }

    private AmazonS3Client CreateAdminClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = ServiceUri.ToString(),
            ForcePathStyle = true,
            AuthenticationRegion = Region,
        };
        return new AmazonS3Client(AccessKeyId, SecretAccessKey, config);
    }
}
