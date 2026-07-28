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
    public void ValidateUniqueRoutes_RejectsMissingOrUnknownIdempotencyStrategy()
    {
        var missing = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
                [new UnspecifiedIdempotencyHandler()]));
        var unknown = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
                [new InvalidIdempotencyHandler()]));

        StringAssert.Contains(missing.Message, "IdempotencyStrategy");
        StringAssert.Contains(unknown.Message, "IdempotencyStrategy");
    }

    [TestMethod]
    public void ValidateUniqueRoutes_RejectsInvalidMetadataAndOverlappingRoutes()
    {
        var emptyEventType = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
                [new InvalidRouteHandler(" ", 1, [])]));
        var invalidSchemaVersion = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
                [new InvalidRouteHandler("fullnet.tenancy.tenant.provisioned", 0, [])]));
        var emptyLegacyEventType = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
                [new InvalidRouteHandler(
                    "fullnet.tenancy.tenant.provisioned",
                    1,
                    [""])]));
        var first = new TenantProvisionedHandler();
        var second = new ConflictingHandler();
        var overlappingRoute = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes([first, second]));
        var duplicateHandlerType = Assert.ThrowsExactly<InvalidOperationException>(() =>
            IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
                [
                    new TenantProvisionedHandler(),
                    new TenantProvisionedHandler()
                ]));

        StringAssert.Contains(emptyEventType.Message, "EventType");
        StringAssert.Contains(invalidSchemaVersion.Message, "SchemaVersion");
        StringAssert.Contains(emptyLegacyEventType.Message, "LegacyEventTypes");
        StringAssert.Contains(
            overlappingRoute.Message,
            "fullnet.tenancy.tenant-provisioned");
        StringAssert.Contains(
            duplicateHandlerType.Message,
            "fullnet.tenancy.tenant.provisioned");
    }

    private sealed class TenantProvisionedHandler(int schemaVersion = 1)
        : IIntegrationEventHandler
    {
        public string EventType => "fullnet.tenancy.tenant.provisioned";

        public IReadOnlyList<string> LegacyEventTypes =>
            ["fullnet.tenancy.tenant-provisioned"];

        public int SchemaVersion => schemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ConflictingHandler : IIntegrationEventHandler
    {
        public string EventType => "fullnet.tenancy.tenant-provisioned";

        public int SchemaVersion => 1;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class InvalidRouteHandler(
        string eventType,
        int schemaVersion,
        IReadOnlyList<string> legacyEventTypes)
        : IIntegrationEventHandler
    {
        public string EventType => eventType;

        public IReadOnlyList<string> LegacyEventTypes => legacyEventTypes;

        public int SchemaVersion => schemaVersion;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class UnspecifiedIdempotencyHandler
        : IIntegrationEventHandler
    {
        public string EventType => "fullnet.test.unspecified";

        public int SchemaVersion => 1;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class InvalidIdempotencyHandler
        : IIntegrationEventHandler
    {
        public string EventType => "fullnet.test.invalid";

        public int SchemaVersion => 1;

        public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
            (IntegrationEventIdempotencyStrategy)99;

        public Task HandleAsync(
            ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
