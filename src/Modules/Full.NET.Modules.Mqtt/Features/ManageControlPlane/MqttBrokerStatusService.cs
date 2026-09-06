using Full.NET.Modules.Mqtt.Configuration;
using Full.NET.Modules.Mqtt.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Mqtt.Features.ManageControlPlane;

/// <summary>读取 MQTT Broker 部署状态与受控发布限额。</summary>
internal sealed class MqttBrokerStatusService(IOptions<MqttBrokerOptions> options)
{
    /// <summary>返回 Broker 配置快照，不回显凭据。</summary>
    /// <returns>Broker 状态响应。</returns>
    public MqttBrokerStatusResponse GetStatus()
    {
        var broker = options.Value;
        return new MqttBrokerStatusResponse(
            broker.Enabled,
            broker.Host,
            broker.Port,
            broker.UseTls,
            broker.MaximumPayloadBytes,
            broker.MaximumPublishRatePerMinute,
            broker.AllowedPublishTopicPrefixes,
            broker.DeploymentNotice);
    }
}
