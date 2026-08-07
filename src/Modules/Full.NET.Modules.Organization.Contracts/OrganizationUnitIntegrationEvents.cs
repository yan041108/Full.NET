using global::MessagePack;

namespace Full.NET.Modules.Organization.Contracts;

/// <summary>Organization 机构单元集成事件的稳定消息类型。</summary>
public static class OrganizationUnitIntegrationEventTypes
{
    /// <summary>租户机构单元状态已提交变更。</summary>
    public const string UnitChanged = "fullnet.organization.unit.changed";
}

/// <summary>表示租户机构单元创建、更新或禁用已与业务状态原子提交。</summary>
[MessagePackObject]
public sealed record OrganizationUnitChangedIntegrationEvent(
    [property: Key(0)] Guid TenantId,
    [property: Key(1)] Guid UnitId,
    [property: Key(2)] string Name,
    [property: Key(3)] bool IsActive,
    [property: Key(4)] long Version,
    [property: Key(5)] DateTimeOffset ChangedAtUtc);
