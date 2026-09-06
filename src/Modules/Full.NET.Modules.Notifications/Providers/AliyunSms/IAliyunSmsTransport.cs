namespace Full.NET.Modules.Notifications.Providers.AliyunSms;

/// <summary>阿里云 dysmsapi SendSms 的受控 HTTP 边界；实现不得记录手机号、模板参数或 Secret。</summary>
internal interface IAliyunSmsTransport
{
    /// <summary>调用 SendSms 并返回 BizId；失败时抛出分类后的 <see cref="AliyunSmsTransportException"/>。</summary>
    /// <param name="command">闭合发送命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<string> SendAsync(
        AliyunSmsSendCommand command,
        CancellationToken cancellationToken);
}

/// <summary>一次 SendSms 调用所需的闭合参数。</summary>
internal sealed record AliyunSmsSendCommand(
    string RegionId,
    string AccessKeyId,
    string AccessKeySecret,
    string PhoneNumber,
    string SignName,
    string TemplateCode,
    string TemplateParamJson,
    string IdempotencyKey);

/// <summary>阿里云 SMS 传输失败分类；Adapter 映射为 Worker 重试语义。</summary>
internal enum AliyunSmsTransportFailureKind
{
    Permanent,
    Transient,
    RateLimited,
}

/// <summary>分类后的阿里云 SMS 传输异常。</summary>
internal sealed class AliyunSmsTransportException(
    AliyunSmsTransportFailureKind failureKind,
    string message) : Exception(message)
{
    public AliyunSmsTransportFailureKind FailureKind { get; } = failureKind;
}
