using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>版本单调的本地隶属读投影；乱序和重复事件不回退状态。</summary>
internal sealed class OrganizationUserUnitChangedIntegrationEventHandler(
    IIntegrationEventSerializer serializer, ICommandExecutor commands,
    IOptions<DatabaseOptions> options) : IIntegrationEventHandler
{
    public string EventType => IdentityOrganizationUserUnitEventTypes.Changed;
    public int SchemaVersion => 1;
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy => IntegrationEventIdempotencyStrategy.NaturallyIdempotent;
    public async Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var value = serializer.Deserialize<IdentityOrganizationUserUnitChangedIntegrationEvent>(payload);
        await commands.ExecuteAsync(options.Value.Provider == DatabaseProvider.MySql ? MySql : SqlServer,
            IdentitySqlParameters.Create(("TenantId", value.TenantId), ("AssignmentId", value.AssignmentId),
                ("UserId", value.UserId), ("UnitId", value.UnitId), ("IsActive", value.IsActive),
                ("SourceVersion", value.Version), ("SourceUpdatedAtUtc", value.ChangedAtUtc)), cancellationToken).ConfigureAwait(false);
    }
    internal static readonly SqlStatement SqlServer = new("identity.organization_user_unit_projection.apply.sql_server", """
        MERGE fn_identity_organization_user_unit_projection WITH (HOLDLOCK) AS target
        USING (SELECT @TenantId AS TenantId, @AssignmentId AS AssignmentId) AS source
        ON target.TenantId = source.TenantId AND target.AssignmentId = source.AssignmentId
        WHEN MATCHED AND target.SourceVersion < @SourceVersion THEN
          UPDATE SET UserId=@UserId, UnitId=@UnitId, IsActive=@IsActive,
                     SourceVersion=@SourceVersion, SourceUpdatedAtUtc=@SourceUpdatedAtUtc
        WHEN NOT MATCHED THEN INSERT (TenantId, AssignmentId, UserId, UnitId, IsActive, SourceVersion, SourceUpdatedAtUtc)
          VALUES (@TenantId,@AssignmentId,@UserId,@UnitId,@IsActive,@SourceVersion,@SourceUpdatedAtUtc);
        """, SqlDataScope.Global);
    internal static readonly SqlStatement MySql = new("identity.organization_user_unit_projection.apply.mysql", """
        INSERT INTO fn_identity_organization_user_unit_projection
          (TenantId,AssignmentId,UserId,UnitId,IsActive,SourceVersion,SourceUpdatedAtUtc)
        VALUES (@TenantId,@AssignmentId,@UserId,@UnitId,@IsActive,@SourceVersion,@SourceUpdatedAtUtc)
        ON DUPLICATE KEY UPDATE
          UserId=IF(SourceVersion < @SourceVersion,@UserId,UserId),
          UnitId=IF(SourceVersion < @SourceVersion,@UnitId,UnitId),
          IsActive=IF(SourceVersion < @SourceVersion,@IsActive,IsActive),
          SourceUpdatedAtUtc=IF(SourceVersion < @SourceVersion,@SourceUpdatedAtUtc,SourceUpdatedAtUtc),
          SourceVersion=GREATEST(SourceVersion,@SourceVersion)
        """, SqlDataScope.Global);
}
