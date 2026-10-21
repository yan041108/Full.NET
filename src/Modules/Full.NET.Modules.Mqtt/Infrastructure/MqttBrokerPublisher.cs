using Full.NET.Modules.Mqtt.Configuration;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace Full.NET.Modules.Mqtt.Infrastructure;

/// <summary>基于 MQTTnet 的受控发布适配器；Broker 未启用时不建立连接。</summary>
internal sealed class MqttBrokerPublisher(IOptions<MqttBrokerOptions> options) : IDisposable
{
    private readonly IMqttClient _client = new MqttFactory().CreateMqttClient();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    /// <summary>向 Broker 发布单条消息。</summary>
    /// <param name="topic">目标主题。</param>
    /// <param name="payload">UTF-8 文本载荷。</param>
    /// <param name="qos">QoS 等级。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发布成功时返回空错误摘要，否则返回失败原因。</returns>
    public async Task<(bool Succeeded, string? ErrorMessage)> PublishAsync(
        string topic,
        string payload,
        int qos,
        CancellationToken cancellationToken)
    {
        var broker = options.Value;
        if (!broker.Enabled)
        {
            return (false, "MQTT broker publishing is disabled.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        cancellationToken = deadline.Token;
        var client = _client;
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(broker.Host, broker.Port);
        if (broker.UseTls)
        {
            builder = builder.WithTlsOptions(o => o.UseTls());
        }

        if (!string.IsNullOrWhiteSpace(broker.Username))
        {
            builder = builder.WithCredentials(broker.Username, broker.Password);
        }

        // 只在建连/重连时互斥，发布仍由 MQTTnet 的并发安全发送队列管理。
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!client.IsConnected)
                await client.ConnectAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        }
        finally { _connectionGate.Release(); }
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qos)
                .Build();
            var result = await client.PublishAsync(message, cancellationToken)
                .ConfigureAwait(false);
            if (result.ReasonCode is MqttClientPublishReasonCode.Success)
            {
                return (true, null);
            }

            return (false, $"MQTT publish rejected: {result.ReasonCode}.");
        }
    }

    /// <summary>宿主释放单例时关闭连接，不逐消息重建 Broker 会话。</summary>
    public void Dispose()
    {
        _client.Dispose();
        _connectionGate.Dispose();
    }
}
