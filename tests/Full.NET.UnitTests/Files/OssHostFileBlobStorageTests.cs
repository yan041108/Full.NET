using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class OssHostFileBlobStorageTests
{
    [TestMethod]
    public async Task Save_uses_staging_object_then_publishes_final_key()
    {
        var client = Substitute.For<IOssBlobClient>();
        client.ExistsAsync("fullnet-files", "host/2026/08/item", Arg.Any<CancellationToken>())
            .Returns(false);
        var storage = CreateStorage(client);
        await using var content = new MemoryStream([1, 2, 3]);

        await storage.SaveAsync("host/2026/08/item", content, CancellationToken.None);

        await client.Received(1).PutAsync(
            "fullnet-files",
            Arg.Is<string>(key =>
                key != null
                && key.StartsWith("host/2026/08/item.", StringComparison.Ordinal)
                && key.EndsWith(".uploading", StringComparison.Ordinal)),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
        await client.Received(1).CopyAsync(
            "fullnet-files",
            Arg.Is<string>(key =>
                key != null && key.EndsWith(".uploading", StringComparison.Ordinal)),
            "host/2026/08/item",
            Arg.Any<CancellationToken>());
        await client.Received(1).DeleteAsync(
            "fullnet-files",
            Arg.Is<string>(key =>
                key != null && key.EndsWith(".uploading", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Save_rejects_existing_final_object()
    {
        var client = Substitute.For<IOssBlobClient>();
        client.ExistsAsync("fullnet-files", "host/exists", Arg.Any<CancellationToken>())
            .Returns(true);
        var storage = CreateStorage(client);

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            storage.SaveAsync("host/exists", new MemoryStream([1]), CancellationToken.None));
        await client.DidNotReceive().PutAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task OpenRead_maps_missing_key_to_file_not_found()
    {
        var client = Substitute.For<IOssBlobClient>();
        client.OpenReadAsync("fullnet-files", "missing", Arg.Any<CancellationToken>())
            .Returns<Task<Stream>>(_ =>
                Task.FromException<Stream>(
                    new FileNotFoundException("Stored blob was not found.", "missing")));
        var storage = CreateStorage(client);

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(() =>
            storage.OpenReadAsync("missing", CancellationToken.None));
    }

    [TestMethod]
    public async Task Delete_is_idempotent()
    {
        var client = Substitute.For<IOssBlobClient>();
        var storage = CreateStorage(client);
        await storage.DeleteAsync("host/gone", CancellationToken.None);
        await client.Received(1).DeleteAsync(
            "fullnet-files",
            "host/gone",
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("/rooted")]
    [DataRow("")]
    public async Task Invalid_storage_keys_are_rejected(string key)
    {
        var storage = CreateStorage(Substitute.For<IOssBlobClient>());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => storage.SaveAsync(key, new MemoryStream([1]), CancellationToken.None));
    }

    private static OssHostFileBlobStorage CreateStorage(IOssBlobClient client)
    {
        var options = new OssFileStorageOptions
        {
            Endpoint = "oss-cn-hangzhou.aliyuncs.com",
            BucketName = "fullnet-files",
        };
        return new OssHostFileBlobStorage(new StaticOptionsMonitor(options), httpClientFactory: null, client);
    }

    private sealed class StaticOptionsMonitor(OssFileStorageOptions current)
        : IOptionsMonitor<OssFileStorageOptions>
    {
        public OssFileStorageOptions CurrentValue { get; } = current;

        public OssFileStorageOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<OssFileStorageOptions, string?> listener) => null;
    }
}

[TestClass]
public sealed class OssFileStorageOptionsValidatorTests
{
    [TestMethod]
    public void Production_requires_object_storage_as_default_provider()
    {
        var environment = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        environment.EnvironmentName.Returns(Microsoft.Extensions.Hosting.Environments.Production);
        var validator = new FileStorageOptionsValidator(
            [
                new StubProvider("local"),
                new StubProvider("s3"),
                new StubProvider("oss"),
            ],
            environment);

        var localResult = validator.Validate(
            null,
            new FileStorageOptions { DefaultProviderKey = "local" });
        Assert.IsTrue(localResult.Failed);
        StringAssert.Contains(localResult.Failures!.First(), "must be 's3' or 'oss'");

        var ossResult = validator.Validate(
            null,
            new FileStorageOptions { DefaultProviderKey = "oss" });
        Assert.IsFalse(ossResult.Failed);
    }

    [TestMethod]
    public void Default_oss_requires_endpoint_bucket_and_credentials()
    {
        var environment = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        environment.EnvironmentName.Returns(Microsoft.Extensions.Hosting.Environments.Development);
        var validator = new OssFileStorageOptionsValidator(
            environment,
            defaultProviderKey: OssHostFileBlobStorage.Key);

        var result = validator.Validate(
            null,
            new OssFileStorageOptions
            {
                Endpoint = "oss-cn-hangzhou.aliyuncs.com",
                BucketName = "bucket",
            });
        Assert.IsTrue(result.Failed);
    }

    private sealed class StubProvider(string providerKey) : IFileStorageProvider
    {
        public string ProviderKey => providerKey;

        public Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
