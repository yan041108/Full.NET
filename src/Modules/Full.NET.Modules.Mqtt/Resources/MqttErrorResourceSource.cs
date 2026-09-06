using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Mqtt.Contracts;

namespace Full.NET.Modules.Mqtt.Resources;

/// <summary>MQTT 模块错误码多语言资源来源。</summary>
internal sealed class MqttErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        MqttErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Mqtt.Resources.MqttErrors",
            typeof(MqttErrorResourceSource).Assembly));
