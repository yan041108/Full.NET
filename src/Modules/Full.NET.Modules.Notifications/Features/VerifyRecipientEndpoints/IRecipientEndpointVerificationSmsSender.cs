using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Notifications.Features.VerifyRecipientEndpoints;

/// <summary>向待验证短信端点发送一次性验证码；实现不得把验证码写入日志或异常。</summary>
internal interface IRecipientEndpointVerificationSmsSender
{
    /// <summary>发送验证码短信；失败时返回稳定错误，不暴露厂商原文响应。</summary>
    /// <param name="providerProfileVersionId">端点绑定的已发布 Profile 版本。</param>
    /// <param name="recipientPhone">解密后的手机号原值，仅用于短信投递。</param>
    /// <param name="code">一次性数字验证码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<bool>> SendAsync(
        Guid providerProfileVersionId,
        string recipientPhone,
        string code,
        CancellationToken cancellationToken);
}
