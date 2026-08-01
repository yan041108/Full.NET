using System.Collections.Concurrent;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class S3HostFileBlobStorageTests
{
    [TestMethod]
    public async Task Save_uses_staging_object_then_publishes_final_key()
    {
        var client = Substitute.For<IS3BlobClient>();
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
        var client = Substitute.For<IS3BlobClient>();
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
        var client = Substitute.For<IS3BlobClient>();
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
        var client = Substitute.For<IS3BlobClient>();
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
        var storage = CreateStorage(Substitute.For<IS3BlobClient>());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => storage.SaveAsync(key, new MemoryStream([1]), CancellationToken.None));
    }

    private static S3HostFileBlobStorage CreateStorage(IS3BlobClient client)
    {
        var options = new S3FileStorageOptions
        {
            EndpointMode = S3EndpointMode.Custom,
            ServiceUrl = "https://minio.example.internal",
            Region = "us-east-1",
            BucketName = "fullnet-files",
            ForcePathStyle = true,
        };
        return new S3HostFileBlobStorage(new StaticOptionsMonitor(options), client);
    }

    private sealed class StaticOptionsMonitor(S3FileStorageOptions current)
        : IOptionsMonitor<S3FileStorageOptions>
    {
        public S3FileStorageOptions CurrentValue { get; } = current;

        public S3FileStorageOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<S3FileStorageOptions, string?> listener) => null;
    }
}

[TestClass]
public sealed class S3FileStorageOptionsValidatorTests
{
    [TestMethod]
    public void Production_requires_s3_as_default_provider()
    {
        var environment = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        environment.EnvironmentName.Returns(Microsoft.Extensions.Hosting.Environments.Production);
        var validator = new FileStorageOptionsValidator(
            [new StubProvider("local"), new StubProvider("s3")],
            environment);

        var result = validator.Validate(
            null,
            new FileStorageOptions { DefaultProviderKey = "local" });
        Assert.IsTrue(result.Failed);
        StringAssert.Contains(result.Failures!.First(), "must be 's3'");
    }

    [TestMethod]
    public void Custom_mode_requires_https_service_url_and_force_path_style()
    {
        var environment = Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>();
        environment.EnvironmentName.Returns(Microsoft.Extensions.Hosting.Environments.Development);
        var validator = new S3FileStorageOptionsValidator(
            environment,
            defaultProviderKey: S3HostFileBlobStorage.Key);

        var result = validator.Validate(
            null,
            new S3FileStorageOptions
            {
                EndpointMode = S3EndpointMode.Custom,
                ServiceUrl = "http://minio.local",
                Region = "us-east-1",
                BucketName = "bucket",
                ForcePathStyle = false,
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
