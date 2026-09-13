using Full.NET.Abstractions.Messaging;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>
/// 为机构单元变更事件显式注册的 Kafka 稳定订阅。
/// </summary>
/// <remarks>
/// 为什么不把所有 legacy handler 自动适配：
/// 真实 Kafka 试点只允许逐一上线——每进入一个生产流必须显式登记 ConsumerName、
/// 幂等策略和持久化投影，防止在未验证 Inbox/CDC 链路的事件流上静默产生不可审计副作用。
/// <see cref="LegacyIntegrationEventHandlerSubscriptionAdapter"/> 保留给全局轮询的向后兼容，
/// 两者在 Worker 并存期间按 Topic 目录的所有权精确分流。
/// 
/// 为什么直接注入具体 Handler 而不是 <see cref="IEnumerable{IIntegrationEventHandler}"/>：
/// 目录构造会解析全部订阅；若订阅在构造期枚举所有 Handler，会连带拉起依赖
/// <see cref="IOutboxWriter"/> 的投影服务并再次解析目录，形成 Scoped DI 环。
/// </remarks>
[IntegrationEventSubscription(
    "fullnet.identity.organization-unit-projection",
    IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
    1)]
internal sealed class OrganizationUnitChangedKafkaSubscription(
    OrganizationUnitChangedIntegrationEventHandler handler)
    : IIntegrationEventSubscription
{
    private readonly OrganizationUnitChangedIntegrationEventHandler _handler = handler;

    /// <summary>稳定 Kafka Consumer Group 标识，进入遥测 consumer_code 标签。</summary>
    public string ConsumerName => "fullnet.identity.organization-unit-projection";

    /// <summary>规范消息类型，与 Organization 发布方常量保持一致。</summary>
    public string EventType =>
        IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged;

    /// <summary>当前订阅处理的载荷模式版本。</summary>
    public int SchemaVersion => 1;

    /// <summary>
    /// 投影写入由 Version 比较天然幂等：重复或乱序消息在
    /// <see cref="OrganizationUnitProjectionWriter.ApplyAsync"/> 中以 no-op 收敛。
    /// </summary>
    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    /// <summary>
    /// 委托给业务 Handler 处理；上下文保持原状继续进入 Inbox 管道用于重复消息探测。
    /// </summary>
    public Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        ((IIntegrationEventHandler)_handler).HandleAsync(
            context,
            payload,
            cancellationToken);
}
