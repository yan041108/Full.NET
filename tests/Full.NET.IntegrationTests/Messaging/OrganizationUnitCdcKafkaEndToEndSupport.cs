using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity;
using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Data.Common;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>
/// Organization 真实写路径 CDC→Kafka→Identity 投影 E2E 共享辅助（双库）。
/// </summary>
internal static class OrganizationUnitCdcKafkaEndToEndSupport
{
    internal const string RealUnitName = "cdc-e2e-unit";

    internal static async Task SeedCdcKafkaStreamOwnershipAsync(DatabaseOptions options)
    {
        var parameters = new
        {
            MessageType = EventDeliveryPilotTestSupport.PilotEventType,
            SchemaVersion = EventDeliveryPilotTestSupport.PilotSchemaVersion,
            TopicCode = IdentityIntegrationEventTopicDefinitions.OrganizationUnitChangedTopicCode,
            CurrentOwner = (int)EventDeliveryOwner.CdcKafka,
            Reason = "organization-cdc-e2e",
        };

        await using var connection = OpenConnection(options);
        if (options.Provider == DatabaseProvider.SqlServer)
        {
            await connection.ExecuteAsync(
                """
                UPDATE fn_messaging_stream_ownership
                SET TopicCode = @TopicCode,
                    CurrentOwner = @CurrentOwner,
                    PreviousOwner = 0,
                    Reason = @Reason,
                    UpdatedAtUtc = SYSUTCDATETIME()
                WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion;

                IF @@ROWCOUNT = 0
                BEGIN
                    INSERT INTO fn_messaging_stream_ownership
                        (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                         CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
                    VALUES
                        (@MessageType, @SchemaVersion, @TopicCode, @CurrentOwner, 0,
                         '00000000-0000-0000-0000-000000000000', SYSUTCDATETIME(),
                         @Reason, SYSUTCDATETIME(), SYSUTCDATETIME());
                END
                """,
                parameters);
            return;
        }

        await connection.ExecuteAsync(
            """
            INSERT INTO fn_messaging_stream_ownership
                (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
                 CutoffEventId, CutoffOccurredAtUtc, Reason, CreatedAtUtc, UpdatedAtUtc)
            VALUES
                (@MessageType, @SchemaVersion, @TopicCode, @CurrentOwner, 0,
                 0x00000000000000000000000000000000, UTC_TIMESTAMP(6),
                 @Reason, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                TopicCode = VALUES(TopicCode),
                CurrentOwner = VALUES(CurrentOwner),
                PreviousOwner = 0,
                Reason = VALUES(Reason),
                UpdatedAtUtc = UTC_TIMESTAMP(6);
            """,
            parameters);
    }

    internal static async Task<Guid> ReadLatestOrganizationOutboxEventIdAsync(
        DatabaseOptions options)
    {
        await using var connection = OpenConnection(options);
        if (options.Provider == DatabaseProvider.SqlServer)
        {
            return await connection.QuerySingleAsync<Guid>(
                """
                SELECT TOP (1) Id
                FROM fn_messaging_outbox_event
                WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
                ORDER BY OccurredAtUtc DESC
                """,
                CreateEventTypeParameters());
        }

        return await connection.QuerySingleAsync<Guid>(
            """
            SELECT Id
            FROM fn_messaging_outbox_event
            WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
            ORDER BY OccurredAtUtc DESC
            LIMIT 1
            """,
            CreateEventTypeParameters());
    }

    internal static async Task<bool> ProjectionExistsAsync(
        DatabaseOptions options,
        Guid tenantId,
        Guid unitId,
        string expectedName)
    {
        await using var connection = OpenConnection(options);
        var name = await connection.QuerySingleOrDefaultAsync<string?>(
            """
            SELECT Name
            FROM fn_identity_organization_unit_projection
            WHERE TenantId = @TenantId AND UnitId = @UnitId
            """,
            new { TenantId = tenantId, UnitId = unitId });
        return string.Equals(name, expectedName, StringComparison.Ordinal);
    }

    internal static async Task<(Guid TenantId, Guid UnitId, string UnitName)>
        CreateOrganizationUnitViaApiAsync(
            FullNetApiFactory factory,
            CancellationToken cancellationToken)
    {
        await factory.InitializeAsync(cancellationToken);
        using var client = factory.CreateClientForHost("localhost");
        var hostToken = await FullNetApiFactoryLogin.LoginAsHostAdminAsync(
            client,
            cancellationToken);
        var tenant = await FullNetApiFactoryLogin.EnterAcmeTenantAsync(
            client,
            hostToken,
            cancellationToken);
        var code = $"cdc-{Guid.NewGuid():N}".ToLowerInvariant();
        var unit = await FullNetApiFactoryOrganization.CreateUnitAsync(
            client,
            tenant.AccessToken,
            code,
            RealUnitName,
            cancellationToken);
        return (tenant.TenantId, unit.Id, unit.Name);
    }

    private static DbConnection OpenConnection(DatabaseOptions options) =>
        options.Provider == DatabaseProvider.SqlServer
            ? new SqlConnection(options.ConnectionString)
            : new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    options.ConnectionString,
                    options.MySqlGuidStorageMode,
                    allowUserVariables: false));

    private static object CreateEventTypeParameters() => new
    {
        MessageType = EventDeliveryPilotTestSupport.PilotEventType,
        SchemaVersion = EventDeliveryPilotTestSupport.PilotSchemaVersion,
    };
}
