using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class NotificationProviderTypeCatalogTests
{
    [TestMethod]
    public void Empty_catalog_rejects_unknown_provider_types_and_channels()
    {
        var catalog = new NotificationProviderTypeCatalog([]);

        Assert.HasCount(0, catalog.All);
        Assert.IsFalse(catalog.TryGet("test.notification", out _));
        Assert.IsFalse(catalog.SupportsChannel("test"));
        Assert.IsFalse(catalog.SupportsChannel("email"));
    }

    [TestMethod]
    public void Registered_adapter_exposes_schema_secret_fields_and_adapter_version()
    {
        var catalog = new NotificationProviderTypeCatalog([new StubAdapter()]);

        Assert.IsTrue(catalog.TryGet("test.notification", out var descriptor));
        Assert.AreEqual("1.0.0", descriptor.AdapterVersion);
        CollectionAssert.Contains(descriptor.SupportedChannelKeys.ToArray(), "test");
        CollectionAssert.Contains(descriptor.SecretFieldKeys.ToArray(), "apiToken");
        Assert.IsTrue(catalog.SupportsChannel("test"));
        Assert.IsFalse(catalog.SupportsChannel("email"));
    }

    private sealed class StubAdapter : INotificationProviderAdapter
    {
        public string? RecipientEndpointKindKey => null;

        public NotificationProviderTypeDescriptor Descriptor { get; } = new(
            "test.notification",
            "1.0.0",
            ["test"],
            [new NotificationProviderConfigField("endpointBaseUrl", "string", true)],
            ["apiToken"],
            true,
            "none");

        public ValueTask<NotificationProviderResult> SendAsync(
            NotificationProviderRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Catalog tests must not send.");
    }
}
