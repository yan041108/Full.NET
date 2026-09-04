using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Features.VerifyRecipientEndpoints;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>集成测试用验证码发送器；捕获验证码原文供断言，不触发真实 SMTP。</summary>
internal sealed class CapturingRecipientEndpointVerificationMailSender : IRecipientEndpointVerificationMailSender
{
    public string? LastCode { get; private set; }

    public string? LastEmail { get; private set; }

    public Task<Result<bool>> SendAsync(
        Guid providerProfileVersionId,
        string recipientEmail,
        string code,
        CancellationToken cancellationToken)
    {
        _ = providerProfileVersionId;
        _ = cancellationToken;
        LastEmail = recipientEmail;
        LastCode = code;
        return Task.FromResult(Result<bool>.Success(true));
    }

    public void Reset()
    {
        LastCode = null;
        LastEmail = null;
    }
}
