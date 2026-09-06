using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Mqtt.Configuration;

/// <summary>校验 MQTT Broker 配置边界，避免负值或空白主机破坏受控发布语义。</summary>
internal sealed class MqttBrokerOptionsValidator : IValidateOptions<MqttBrokerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MqttBrokerOptions options)
    {
        if (options.Port is < 1 or > 65535)
        {
            return ValidateOptionsResult.Fail("FullNet:Mqtt:Broker:Port must be between 1 and 65535.");
        }

        if (options.MaximumPayloadBytes <= 0)
        {
            return ValidateOptionsResult.Fail(
                "FullNet:Mqtt:Broker:MaximumPayloadBytes must be greater than zero.");
        }

        if (options.MaximumPublishRatePerMinute <= 0)
        {
            return ValidateOptionsResult.Fail(
                "FullNet:Mqtt:Broker:MaximumPublishRatePerMinute must be greater than zero.");
        }

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Host))
        {
            return ValidateOptionsResult.Fail(
                "FullNet:Mqtt:Broker:Host is required when MQTT publishing is enabled.");
        }

        if (options.AllowedPublishTopicPrefixes.Count == 0)
        {
            return ValidateOptionsResult.Fail(
                "FullNet:Mqtt:Broker:AllowedPublishTopicPrefixes must contain at least one prefix.");
        }

        return ValidateOptionsResult.Success;
    }
}
