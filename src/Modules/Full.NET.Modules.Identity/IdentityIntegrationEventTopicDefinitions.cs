using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity;

/// <summary>
/// Identity 消费方订阅所需的官方 Integration Event Topic 目录条目。
/// </summary>
internal static class IdentityIntegrationEventTopicDefinitions
{
    public const string OrganizationUnitChangedTopicCode = "organization.unit-changed.v1";

    public static IntegrationEventTopicDefinition OrganizationUnitChanged { get; } =
        IntegrationEventTopicDefinition.Create(
            OrganizationUnitChangedTopicCode,
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
            1,
            EventDeliveryOwner.LegacyPolling);
}
