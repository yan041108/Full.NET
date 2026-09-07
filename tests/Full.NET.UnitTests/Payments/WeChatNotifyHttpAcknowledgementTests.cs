using System.Reflection;
using Full.NET.Modules.Payments.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.UnitTests.Payments;

/// <summary>支付平台依据 HTTP 状态决定重试，失败应答不能被成功状态提前确认。</summary>
[TestClass]
public sealed class WeChatNotifyHttpAcknowledgementTests
{
    /// <summary>校验成功与失败应答同时保持 HTTP 和业务协议语义。</summary>
    /// <param name="code">业务应答码。</param>
    /// <param name="expectedStatus">支付平台应观察到的 HTTP 状态。</param>
    [TestMethod]
    [DataRow("SUCCESS", StatusCodes.Status200OK)]
    [DataRow("FAIL", StatusCodes.Status500InternalServerError)]
    public void Acknowledgement_status_preserves_retry_semantics(string code, int expectedStatus)
    {
        var endpoint = typeof(Full.NET.Modules.Payments.PaymentsModule).Assembly.GetType(
            "Full.NET.Modules.Payments.Features.ReceiveWeChatNotify.Endpoint", throwOnError: true)!;
        var result = endpoint.GetMethod("JsonAck", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [new WeChatPayNotifyAckResponse(code, "test")]);
        Assert.AreEqual(expectedStatus, ((IStatusCodeHttpResult)result!).StatusCode ?? StatusCodes.Status200OK);
    }
}
