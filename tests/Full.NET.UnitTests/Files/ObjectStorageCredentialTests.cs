using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>静态身份完整性必须在启动和客户端构造时一致，禁止跨来源拼接密钥。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ObjectStorageCredentialTests
{
    [TestMethod]
    [DataRow("Files", "access")]
    [DataRow("Files", "secret")]
    [DataRow("Files", "session")]
    [DataRow("AWS", "access")]
    [DataRow("AWS", "secret")]
    [DataRow("AWS", "session")]
    public void Incomplete_static_identity_is_rejected_even_with_workload_hints(string source, string field)
    {
        using var environment = new CredentialEnvironment();
        environment.Set(Key(source, field), "credential-sentinel");
        environment.Set("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/not-requested");
        environment.Set("AWS_WEB_IDENTITY_TOKEN_FILE", "/not-opened");
        var error = Assert.Throws<InvalidOperationException>(() => S3HostFileBlobStorage.ResolveCredentials());
        Assert.IsFalse(error.Message.Contains("credential-sentinel", StringComparison.Ordinal));
        Assert.IsTrue(S3Validator().Validate(null, S3Options()).Failed);
    }

    [TestMethod]
    [DataRow("access")]
    [DataRow("secret")]
    [DataRow("session")]
    public void Dedicated_partial_identity_never_borrows_from_a_complete_aws_identity(string field)
    {
        using var environment = new CredentialEnvironment();
        Pair(environment, "AWS"); environment.Set("AWS_SESSION_TOKEN", "aws-session");
        environment.Set(Key("Files", field), "dedicated-sentinel");
        Assert.Throws<InvalidOperationException>(() => S3HostFileBlobStorage.ResolveCredentials());
        var result = S3Validator().Validate(null, S3Options());
        Assert.IsTrue(result.Failed);
        Assert.IsFalse(string.Join(" ", result.Failures!).Contains("sentinel", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Explicit_whitespace_dedicated_identity_does_not_silently_use_aws()
    {
        using var environment = new CredentialEnvironment(); Pair(environment, "AWS");
        environment.Set(S3FileStorageOptions.AccessKeyEnvironmentVariable, " ");
        Assert.Throws<InvalidOperationException>(() => S3HostFileBlobStorage.ResolveCredentials());
        Assert.IsTrue(S3Validator().Validate(null, S3Options()).Failed);
    }

    [TestMethod]
    [DataRow("Files")]
    [DataRow("AWS")]
    public void Explicit_whitespace_session_token_is_rejected(string source)
    {
        using var environment = new CredentialEnvironment(); Pair(environment, source);
        environment.Set(Key(source, "session"), " ");
        Assert.Throws<InvalidOperationException>(() => S3HostFileBlobStorage.ResolveCredentials());
        Assert.IsTrue(S3Validator().Validate(null, S3Options()).Failed);
    }

    [TestMethod]
    [DataRow("Files")]
    [DataRow("AWS")]
    public async Task Complete_static_pair_preserves_the_selected_identity(string source)
    {
        using var environment = new CredentialEnvironment(); Pair(environment, source);
        var resolved = S3HostFileBlobStorage.ResolveCredentials(); Assert.IsNotNull(resolved);
        var credentials = await resolved.GetCredentialsAsync();
        Assert.AreEqual(source + "-access", credentials.AccessKey);
        Assert.AreEqual(source + "-secret", credentials.SecretKey);
        Assert.IsTrue(string.IsNullOrEmpty(credentials.Token));
        Assert.IsTrue(S3Validator().Validate(null, S3Options()).Succeeded);
    }

    [TestMethod]
    [DataRow("Files")]
    [DataRow("AWS")]
    public async Task Complete_session_identity_preserves_its_own_token(string source)
    {
        using var environment = new CredentialEnvironment(); Pair(environment, source);
        environment.Set(Key(source, "session"), source + "-session");
        var resolved = S3HostFileBlobStorage.ResolveCredentials(); Assert.IsNotNull(resolved);
        var credentials = await resolved.GetCredentialsAsync();
        Assert.AreEqual(source + "-session", credentials.Token);
        Assert.AreEqual(source + "-access", credentials.AccessKey);
        Assert.IsTrue(S3Validator().Validate(null, S3Options()).Succeeded);
    }

    [TestMethod]
    public async Task Dedicated_pair_does_not_inherit_an_unrelated_aws_session_token()
    {
        using var environment = new CredentialEnvironment(); Pair(environment, "Files"); Pair(environment, "AWS");
        environment.Set("AWS_SESSION_TOKEN", "unrelated-session");
        var credentials = await S3HostFileBlobStorage.ResolveCredentials()!.GetCredentialsAsync();
        Assert.AreEqual("Files-access", credentials.AccessKey);
        Assert.IsTrue(string.IsNullOrEmpty(credentials.Token));
    }

    [TestMethod]
    public void Absent_static_credentials_leave_the_existing_workload_boundary_unchanged()
    {
        using var environment = new CredentialEnvironment();
        environment.Set("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "/not-requested");
        Assert.IsNull(S3HostFileBlobStorage.ResolveCredentials());
        Assert.IsTrue(S3Validator().Validate(null, S3Options()).Succeeded);
    }

    [TestMethod]
    public void Configured_storage_without_any_credential_source_is_rejected()
    {
        using var environment = new CredentialEnvironment();
        Assert.IsNull(S3HostFileBlobStorage.ResolveCredentials());
        Assert.IsTrue(S3Validator().Validate(null, S3Options()).Failed);
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("secret")]
    [DataRow("blank-id")]
    [DataRow("blank-secret")]
    public void Oss_partial_pair_fails_startup_and_client_creation(string field)
    {
        using var environment = new CredentialEnvironment();
        environment.Set(OssFileStorageOptions.AccessKeyIdEnvironmentVariable, field == "id" ? null : field == "blank-id" ? " " : "oss-id");
        environment.Set(OssFileStorageOptions.AccessKeySecretEnvironmentVariable, field == "secret" ? null : field == "blank-secret" ? " " : "oss-secret");
        var options = new OssFileStorageOptions { BucketName = "bucket", Endpoint = "https://storage.example.test" };
        Assert.IsTrue(new OssFileStorageOptionsValidator(Host(), "oss").Validate(null, options).Failed);
        var clients = Substitute.For<IHttpClientFactory>();
        Assert.Throws<InvalidOperationException>(() => HttpOssBlobClient.Create(clients, options));
        clients.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [TestMethod]
    public void Oss_complete_pair_passes_without_creating_a_network_client()
    {
        using var environment = new CredentialEnvironment();
        environment.Set(OssFileStorageOptions.AccessKeyIdEnvironmentVariable, "oss-id");
        environment.Set(OssFileStorageOptions.AccessKeySecretEnvironmentVariable, "oss-secret");
        var options = new OssFileStorageOptions { BucketName = "bucket", Endpoint = "https://storage.example.test" };
        Assert.IsTrue(new OssFileStorageOptionsValidator(Host(), "oss").Validate(null, options).Succeeded);
        var clients = Substitute.For<IHttpClientFactory>();
        Assert.IsNotNull(HttpOssBlobClient.Create(clients, options));
        clients.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    [TestMethod]
    public void Unconfigured_alternative_storage_stays_optional_with_unrelated_environment_keys()
    {
        using var environment = new CredentialEnvironment();
        environment.Set(S3FileStorageOptions.AccessKeyEnvironmentVariable, "unrelated-s3");
        environment.Set(OssFileStorageOptions.AccessKeyIdEnvironmentVariable, "unrelated-oss");
        Assert.IsTrue(new S3FileStorageOptionsValidator(Host(), "oss").Validate(null, new()).Succeeded);
        Assert.IsTrue(new OssFileStorageOptionsValidator(Host(), "s3").Validate(null, new()).Succeeded);
    }

    private static S3FileStorageOptionsValidator S3Validator() => new(Host(), "s3");
    private static S3FileStorageOptions S3Options() => new() { BucketName = "bucket", Region = "us-east-1" };
    private static IHostEnvironment Host()
    {
        var host = Substitute.For<IHostEnvironment>(); host.EnvironmentName.Returns(Environments.Production); return host;
    }
    private static void Pair(CredentialEnvironment environment, string source)
    { environment.Set(Key(source, "access"), source + "-access"); environment.Set(Key(source, "secret"), source + "-secret"); }
    private static string Key(string source, string field) => (source, field) switch
    {
        ("Files", "access") => S3FileStorageOptions.AccessKeyEnvironmentVariable,
        ("Files", "secret") => S3FileStorageOptions.SecretKeyEnvironmentVariable,
        ("Files", "session") => S3FileStorageOptions.SessionTokenEnvironmentVariable,
        ("AWS", "access") => "AWS_ACCESS_KEY_ID", ("AWS", "secret") => "AWS_SECRET_ACCESS_KEY", ("AWS", "session") => "AWS_SESSION_TOKEN",
        _ => throw new InvalidOperationException("未知凭据测试来源"),
    };

    // 仅隔离本测试进程的固定键；恢复原值，不读取配置到输出或修改机器级环境。
    private sealed class CredentialEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> previous = new();
        internal CredentialEnvironment()
        {
            string[] keys = [S3FileStorageOptions.AccessKeyEnvironmentVariable, S3FileStorageOptions.SecretKeyEnvironmentVariable,
                S3FileStorageOptions.SessionTokenEnvironmentVariable, "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN",
                "AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", "AWS_WEB_IDENTITY_TOKEN_FILE",
                OssFileStorageOptions.AccessKeyIdEnvironmentVariable, OssFileStorageOptions.AccessKeySecretEnvironmentVariable];
            foreach (var key in keys) { previous.Add(key, Environment.GetEnvironmentVariable(key)); Environment.SetEnvironmentVariable(key, null); }
        }
        internal void Set(string key, string? value) => Environment.SetEnvironmentVariable(key, value);
        public void Dispose() { foreach (var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
    }
}
