using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageStorageProviders;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class FileStorageProviderCatalogServiceTests
{
    [TestMethod]
    public void List_includes_registered_providers_with_default_flag()
    {
        var service = CreateService(defaultProviderKey: "oss");
        var items = service.List();

        Assert.AreEqual(3, items.Count);
        var oss = items.Single(item => item.ProviderKey == "oss");
        Assert.IsTrue(oss.IsDefault);
        Assert.AreEqual("oss", oss.Kind);
        Assert.IsTrue(oss.SupportsConnectivityTest);
        Assert.IsFalse(items.Single(item => item.ProviderKey == "local").SupportsConnectivityTest);
    }

    [TestMethod]
    public async Task TestConnectivity_rejects_local_provider()
    {
        var service = CreateService(defaultProviderKey: "local");
        var result = await service.TestConnectivityAsync("local", CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Value!.Succeeded);
        StringAssert.Contains(result.Value.Message, "does not support");
    }

    [TestMethod]
    public async Task TestConnectivity_returns_not_found_for_unknown_provider()
    {
        var service = CreateService(defaultProviderKey: "local");
        var result = await service.TestConnectivityAsync("missing", CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.StorageProviderNotFound, result.Error!.Code);
    }

    private static FileStorageProviderCatalogService CreateService(string defaultProviderKey)
    {
        var providers = new IFileStorageProvider[]
        {
            new StubProvider(LocalHostFileBlobStorage.Key),
            new StubProvider(S3HostFileBlobStorage.Key),
            new StubProvider(OssHostFileBlobStorage.Key),
        };
        var registry = new FileStorageProviderRegistry(
            providers,
            Options.Create(new FileStorageOptions { DefaultProviderKey = defaultProviderKey }));
        return new FileStorageProviderCatalogService(
            providers,
            registry,
            new StaticOptionsMonitor<LocalFileStorageOptions>(new LocalFileStorageOptions { RootPath = "/data/files" }),
            new StaticOptionsMonitor<S3FileStorageOptions>(new S3FileStorageOptions
            {
                EndpointMode = S3EndpointMode.Aws,
                Region = "us-east-1",
                BucketName = "fullnet-files",
            }),
            new StaticOptionsMonitor<OssFileStorageOptions>(new OssFileStorageOptions
            {
                Endpoint = "oss-cn-hangzhou.aliyuncs.com",
                BucketName = "fullnet-files",
            }),
            Substitute.For<IHttpClientFactory>());
    }

    private sealed class StaticOptionsMonitor<T>(T current) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = current;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
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
