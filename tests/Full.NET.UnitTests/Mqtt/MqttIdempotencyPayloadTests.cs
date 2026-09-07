using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Mqtt.Contracts;
using Full.NET.Modules.Mqtt.Features.ManageControlPlane;
using Full.NET.Modules.Mqtt.Persistence;

namespace Full.NET.UnitTests.Mqtt;

/// <summary>幂等比较必须绑定完整消息，历史缺失摘要时不能推断正文相同。</summary>
[TestClass]
public sealed class MqttIdempotencyPayloadTests
{
    /// <summary>同长度正文、Unicode 正文和历史记录均按摘要判定。</summary>
    /// <param name="payload">重试正文。</param>
    /// <param name="hasDigest">记录是否具有可验证摘要。</param>
    /// <param name="expected">是否匹配首次正文。</param>
    [TestMethod]
    [DataRow("aa", true, true)]
    [DataRow("bb", true, false)]
    [DataRow("é", true, false)]
    [DataRow("aa", false, false)]
    public void Replay_requires_full_payload_digest(string payload, bool hasDigest, bool expected)
    {
        var record = new MqttMessageRecord { Topic = "host/test", PayloadSizeBytes = 2, Qos = 1 };
        typeof(MqttMessageRecord).GetProperty("PayloadDigest")?.SetValue(record,
            hasDigest ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("aa"))) : null);
        var request = new PublishMqttMessageRequest("host/test", payload, 1, null, "key");
        var actual = (bool)typeof(MqttMessagePublishService)
            .GetMethod("MatchesIdempotentReplay", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [record, request, "host/test"])!;
        Assert.AreEqual(expected, actual);
    }
}
