using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Messaging;

/// <summary>消息运维与试点切流注册的官方 Topic 目录条目。</summary>
internal static class MessagingTopicDefinitions
{
    public const string OrganizationUnitChangedTopicCode = "organization.unit-changed.v1";

    public static IntegrationEventTopicDefinition OrganizationUnitChanged { get; } =
        IntegrationEventTopicDefinition.Create(
            OrganizationUnitChangedTopicCode,
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
            1,
            EventDeliveryOwner.LegacyPolling);
}
