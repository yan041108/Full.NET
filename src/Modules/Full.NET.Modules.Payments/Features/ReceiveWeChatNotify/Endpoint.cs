using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Payments.Connectivity;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Payments.Features.ReceiveWeChatNotify;

/// <summary>微信支付结果通知匿名入口。</summary>
internal static class Endpoint
{
    private const int MaxCallbackBodyBytes = 32 * 1024;
    private static readonly PaymentsJsonSerializerContext CallbackJsonContext = new(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>注册微信 Native 支付通知路由。</summary>
    /// <param name="endpoints">路由构建器。</param>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
                "/api/v1/payments/wechat-native/notify/{merchantConfigId:guid}",
                async (
                    Guid merchantConfigId,
                    PaymentWeChatNotifyService service,
                    HttpContext httpContext,
                    CancellationToken cancellationToken) =>
                {
                    if (httpContext.Request.ContentLength is { } contentLength
                        && contentLength > MaxCallbackBodyBytes)
                    {
                        return JsonAck(new WeChatPayNotifyAckResponse("FAIL", "Payload too large."));
                    }

                    using var buffer = new MemoryStream();
                    var block = new byte[8192];
                    while (true)
                    {
                        var read = await httpContext.Request.Body.ReadAsync(block, cancellationToken)
                            .ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        if (buffer.Length + read > MaxCallbackBodyBytes)
                        {
                            return JsonAck(new WeChatPayNotifyAckResponse("FAIL", "Payload too large."));
                        }

                        buffer.Write(block, 0, read);
                    }

                    var rawBody = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
                    var timestamp = httpContext.Request.Headers["Wechatpay-Timestamp"].ToString();
                    var nonce = httpContext.Request.Headers["Wechatpay-Nonce"].ToString();
                    var signature = httpContext.Request.Headers["Wechatpay-Signature"].ToString();
                    var platformSerial = httpContext.Request.Headers["Wechatpay-Serial"].ToString();
                    if (string.IsNullOrWhiteSpace(timestamp)
                        || string.IsNullOrWhiteSpace(nonce)
                        || string.IsNullOrWhiteSpace(signature)
                        || string.IsNullOrWhiteSpace(platformSerial))
                    {
                        return JsonAck(new WeChatPayNotifyAckResponse("FAIL", "Missing WeChat notify headers."));
                    }

                    var ack = await service.HandleAsync(
                            merchantConfigId,
                            httpContext.Request.Path.Value ?? string.Empty,
                            timestamp,
                            nonce,
                            signature,
                            platformSerial,
                            rawBody,
                            cancellationToken)
                        .ConfigureAwait(false);
                    return JsonAck(ack);
                })
            .WithName("paymentsWeChatNativeNotify")
            .WithTags("PaymentWeChatNotify")
            .AllowAnonymous();
    }

    /// <summary>用闭合元数据输出小写 JSON 协议，并让失败 HTTP 状态保留支付平台的重试机会。</summary>
    /// <param name="ack">通知处理应答。</param>
    /// <returns>保持现有协议字段的 JSON 结果。</returns>
    private static IResult JsonAck(WeChatPayNotifyAckResponse ack) =>
        Results.Json(
            ack,
            CallbackJsonContext.WeChatPayNotifyAckResponse,
            statusCode: string.Equals(ack.Code, "SUCCESS", StringComparison.Ordinal)
                ? StatusCodes.Status200OK
                : StatusCodes.Status500InternalServerError);
}
