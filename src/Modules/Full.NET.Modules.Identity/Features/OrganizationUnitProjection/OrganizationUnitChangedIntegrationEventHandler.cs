using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>消费 Organization 机构单元变更事件并写入 Identity 本地投影。</summary>
internal sealed class OrganizationUnitChangedIntegrationEventHandler(
    IIntegrationEventSerializer serializer,
    OrganizationUnitProjectionWriter writer) : IIntegrationEventHandler
{
    public string EventType =>
        IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged;

    public int SchemaVersion => 1;

    // 版本比较使重复与乱序消息收敛为 no-op。
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        writer.ApplyAsync(
            serializer.Deserialize<IdentityOrganizationUnitChangedIntegrationEvent>(payload),
            cancellationToken);
}
