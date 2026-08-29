using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Messaging.Abstractions;
using Full.NET.Migrations.DbUp;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using global::MemoryPack;

namespace Full.NET.IntegrationTests.Messaging;

internal static partial class MessagingOutboxTestSupport
{
    internal const string TestEventType = "fullnet.messaging.outbox.test.event";
    internal const int TestSchemaVersion = 1;

    [MemoryPackable]
    internal partial record MessagingOutboxTestPayload(string Value);

    internal static async Task MigrateAsync(DatabaseOptions options)
    {
        var runner = new DbUpMigrationRunner(
            Options.Create(options),
            NullLoggerFactory.Instance,
            MigrationContractOptionFactory.UuidOptions(),
            MigrationContractOptionFactory.NamingOptions());
        var result = await runner.MigrateAsync();
        Assert.IsTrue(result.Successful);
    }

    internal static IConfiguration CreateConfiguration(DatabaseOptions options) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = options.Provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = options.ConnectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    options.MySqlGuidStorageMode.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
                [$"{MessagingOutboxOptions.SectionName}:Mode"] =
                    MessagingOutboxMode.AppendOnlyV2.ToString(),
            })
            .Build();

    internal static ServiceProvider BuildAppendOnlyServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        services.RemoveAll<IEffectiveEventDeliveryOwnerResolver>();
        services.AddSingleton<IEffectiveEventDeliveryOwnerResolver, AppendOnlyCdcOwnerResolver>();
        services.RemoveAll<IEventStreamOwnershipGate>();
        services.AddSingleton<IEventStreamOwnershipGate, PermissiveEventStreamOwnershipGate>();
        services.AddFullNetMemoryPack();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    internal static IntegrationEventMetadata CreateMetadata(string partitionKey) =>
        IntegrationEventMetadata.Create(
            partitionKey,
            "fullnet.messaging.tests",
            correlationId: "messaging-outbox-test");

    private sealed class AppendOnlyCdcOwnerResolver : IEffectiveEventDeliveryOwnerResolver
    {
        public Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EventDeliveryOwner.CdcKafka);
    }

    private sealed class PermissiveEventStreamOwnershipGate : IEventStreamOwnershipGate
    {
        public Task<bool> AcquireProducerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireConsumerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireOwnershipChangeAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
