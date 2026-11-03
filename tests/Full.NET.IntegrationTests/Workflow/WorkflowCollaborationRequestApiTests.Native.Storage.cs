using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod, TestCategory("NativeWorkflow")]
    public Task Workflow_native_s3_collaboration_with_sql_server() => VerifyNativeWorkflowAsync(DatabaseProvider.SqlServer, s3: true);

    [TestMethod, TestCategory("NativeWorkflow")]
    public Task Workflow_native_s3_collaboration_with_mysql() => VerifyNativeWorkflowAsync(DatabaseProvider.MySql, s3: true);

    /// <summary>仅为当前验收创建独占桶；真实ELF走S3签名HTTP，夹具不代替API上传或返回业务文件。</summary>
    private sealed class NativeWorkflowS3(IContainer container, AmazonS3Client client, string bucket,
        IReadOnlyDictionary<string, string> settings) : IAsyncDisposable
    {
        public IReadOnlyDictionary<string, string> NativeSettings { get; } = settings;

        public static async Task<NativeWorkflowS3> StartAsync()
        {
            if (Environment.GetEnvironmentVariable("FULLNET_TESTCONTAINERS_BOUNDED") != "1")
                throw new InvalidOperationException("Workflow S3验收必须使用已握手的独立资源会话。");
            var image = Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_S3_IMAGE");
            if (image is null || !System.Text.RegularExpressions.Regex.IsMatch(image, "^sha256:[a-f0-9]{64}$"))
                throw new InvalidOperationException("必须提供已冻结的MinIO镜像Id。");
            var access = "fn" + Guid.CreateVersion7().ToString("N")[..18];
            var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
            var bucket = "fn-workflow-" + Guid.CreateVersion7().ToString("N");
            var container = new ContainerBuilder(image).WithCommand("server", "/data", "--console-address", ":9001")
                .WithEnvironment("MINIO_ROOT_USER", access).WithEnvironment("MINIO_ROOT_PASSWORD", secret)
                .WithEnvironment("MINIO_BROWSER", "off").WithEnvironment("GOMAXPROCS", "1").WithEnvironment("GOMEMLIMIT", "256MiB")
                .WithLabel("fullnet.workflow.storage.role", "s3").WithPortBinding(9000, true)
                .WithCreateParameterModifier(parameters =>
                {
                    SharedDatabaseFixture.ApplyBoundedOwnership(parameters.Labels ??= new Dictionary<string, string>());
                    var limits = parameters.HostConfig ??= new();
                    limits.Memory = 512L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
                    // 小型HTTP功能验收不持有宿主卷；临时对象随独占容器回收，不进入仓库或持久缓存。
                    limits.Tmpfs = new Dictionary<string, string> { ["/data"] = "rw,size=268435456,nosuid,nodev,noexec" };
                })
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9000).ForPath("/minio/health/ready")))
                .Build();
            AmazonS3Client? client = null;
            try
            {
                await container.StartAsync();
                var port = container.GetMappedPublicPort(9000);
                client = new AmazonS3Client(access, secret, new AmazonS3Config
                {
                    ServiceURL = $"http://127.0.0.1:{port}", ForcePathStyle = true, AuthenticationRegion = "us-east-1",
                });
                await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
                return new(container, client, bucket, new Dictionary<string, string>
                {
                    ["Files__Storage__DefaultProviderKey"] = "s3", ["Files__S3__EndpointMode"] = "Custom",
                    ["Files__S3__ServiceUrl"] = $"http://host.docker.internal:{port}", ["Files__S3__Region"] = "us-east-1",
                    ["Files__S3__ForcePathStyle"] = "true", ["Files__S3__AllowInsecureServiceUrl"] = "true",
                    ["Files__S3__BucketName"] = bucket, ["Files__S3__AccessKeyId"] = access, ["Files__S3__SecretAccessKey"] = secret,
                });
            }
            catch
            {
                client?.Dispose(); await container.DisposeAsync(); throw;
            }
        }

        public async Task AssertObjectsAsync(byte[] expected, int expectedCount)
        {
            var objects = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket });
            Assert.IsTrue(objects.IsTruncated is false, "独占功能验收桶不应出现未检查的对象分页。");
            Assert.HasCount(expectedCount, objects.S3Objects, "上传必须真实进入S3；元数据行或本地文件不能代替对象。");
            foreach (var item in objects.S3Objects)
            {
                using var response = await client.GetObjectAsync(bucket, item.Key);
                using var bytes = new MemoryStream(); await response.ResponseStream.CopyToAsync(bytes);
                CollectionAssert.AreEqual(expected, bytes.ToArray(), "实际S3对象与Native上传字节不一致。");
            }
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose(); await container.DisposeAsync();
        }
    }
}
