using Full.NET.Abstractions.Messaging;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class IntegrationEventHandlerMatcherTests
{
    [TestMethod]
    public void Match_ReturnsHandlerForCanonicalAndLegacyTypes()
    {
        var handler = new TenantProvisionedHandler();

        var canonical = IntegrationEventHandlerMatcher.Match(
            [handler],
            "fullnet.tenancy.tenant.provisioned",
            1);
        var legacy = IntegrationEventHandlerMatcher.Match(
            [handler],
            "fullnet.tenancy.tenant-provisioned",
            1);

        Assert.HasCount(1, canonical);
        Assert.HasCount(1, legacy);
        Assert.AreSame(handler, canonical[0]);
        Assert.AreSame(handler, legacy[0]);
    }

    [TestMethod]
    public void Match_ReturnsOnlyExactSchemaVersionWhenParallelVersionsExist()
    {
        var v1 = new TenantProvisionedHandler(schemaVersion: 1);
        var v2 = new TenantProvisionedHandler(schemaVersion: 2);

        var matches = IntegrationEventHandlerMatcher.Match(
            [v1, v2],
            "fullnet.tenancy.tenant.provisioned",
            2);

        Assert.HasCount(1, matches);
        Assert.AreSame(v2, matches[0]);
    }

    [TestMethod]
    public void ValidateUniqueRoutes_AllowsParallelVersionsForSameEventType()
    {
        var v1 = new TenantProvisionedHandler(schemaVersion: 1);
        var v2 = new TenantProvisionedHandler(schemaVersion: 2);

        IntegrationEventHandlerMatcher.ValidateUniqueRoutes([v1, v2]);
    }

    [TestMethod]
    public void ValidateUniqueRoutes_RejectsOverlappingLegacyRoutes()
    {
        var first = new TenantProvisionedHandler();
        var second = new ConflictingHandler();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes([first, second]));

        StringAssert.Contains(exception.Message, "fullnet.tenancy.tenant-provisioned");
    }

    private sealed class TenantProvisionedHandler(int schemaVersion = 1)
        : IIntegrationEventHandler
    {
        public string EventType => "fullnet.tenancy.tenant.provisioned";

        public IReadOnlyList<string> LegacyEventTypes =>
            ["fullnet.tenancy.tenant-provisioned"];

        public int SchemaVersion => schemaVersion;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ConflictingHandler : IIntegrationEventHandler
    {
        public string EventType => "fullnet.tenancy.tenant-provisioned";

        public int SchemaVersion => 1;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
