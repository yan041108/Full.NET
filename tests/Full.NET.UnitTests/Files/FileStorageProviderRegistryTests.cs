using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class FileStorageProviderRegistryTests
{
    [TestMethod]
    public void Duplicate_provider_keys_are_rejected_case_insensitively()
    {
        var providers = new IFileStorageProvider[]
        {
            new StubProvider("local"),
            new StubProvider("LOCAL"),
        };

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => CreateRegistry(providers, "local"));
    }

    [TestMethod]
    public void Unknown_default_provider_is_rejected()
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => CreateRegistry([new StubProvider("local")], "missing"));
    }

    [TestMethod]
    public void Unknown_stored_provider_is_rejected()
    {
        var registry = CreateRegistry([new StubProvider("local")], "local");

        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => registry.Resolve("missing"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Local")]
    [DataRow("local-provider")]
    [DataRow("local/provider")]
    public void Noncanonical_provider_keys_are_rejected(string providerKey)
    {
        _ = Assert.ThrowsExactly<InvalidOperationException>(
            () => CreateRegistry([new StubProvider(providerKey)], providerKey));
    }

    [TestMethod]
    public void Configured_default_and_stored_provider_resolve_registered_instances()
    {
        var local = new StubProvider("local");
        var archive = new StubProvider("archive_1");
        var registry = CreateRegistry([local, archive], "archive_1");

        Assert.AreSame(archive, registry.DefaultProvider);
        Assert.AreSame(local, registry.Resolve("local"));
    }

    private static FileStorageProviderRegistry CreateRegistry(
        IEnumerable<IFileStorageProvider> providers,
        string defaultProviderKey) =>
        new(
            providers,
            Options.Create(new FileStorageOptions
            {
                DefaultProviderKey = defaultProviderKey,
            }));

    private sealed class StubProvider(string providerKey) : IFileStorageProvider
    {
        public string ProviderKey => providerKey;

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
