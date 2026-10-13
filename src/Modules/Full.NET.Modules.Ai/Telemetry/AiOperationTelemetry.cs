using Full.NET.Abstractions.Results;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Telemetry;

/// <summary>沿用原操作边界和身份；仅记录固定结果，不拥有预算、事务或响应交付。</summary>
internal static class AiOperationTelemetry
{
    /// <summary>预取消不创建观测；异常原样传播，诊断释放不能覆盖原执行结果。</summary>
    internal static async Task<Result<T>> RunAsync<T>(string stage,
        Func<AgentExecutionTelemetry.Observation, Task<Result<T>>> action, CancellationToken token, bool completedOnSuccess = true)
    {
        token.ThrowIfCancellationRequested();
        using var observation = AgentExecutionTelemetry.Start(Guid.Empty, stage, cancellationToken: token);
        try
        {
            var result = await action(observation).ConfigureAwait(false);
            // 准入边界已经识别的预算拒绝优先于兼容层错误码和 HTTP 错误类别。
            if (!result.IsSuccess)
            {
                if (observation.Outcome == "failed") observation.Outcome = Outcome(result.Error);
            }
            else if (completedOnSuccess) observation.Outcome = "completed";
            return result;
        }
        catch (UnauthorizedAccessException) { observation.Outcome = "authorization_required"; throw; }
        catch (HostContextRequiredException) { observation.Outcome = "authorization_required"; throw; }
        catch (OperationCanceledException) { observation.Outcome = "cancelled"; throw; }
        // 派发后的结算异常不是准入拒绝；原异常及收尾责任保持不变。
        catch (Exception) { observation.Outcome = "failed"; throw; }
    }

    /// <summary>只读取稳定机器码及错误类别；正文、异常消息和调用方身份不进入标签。</summary>
    internal static string Outcome(Error? error) => error switch
    {
        { Code: var code } when code.StartsWith("ai.budget.", StringComparison.Ordinal) => "budget_rejected",
        { Type: ErrorType.Unauthorized or ErrorType.Forbidden } => "authorization_required",
        { Code: AiErrorCodes.ChatGenerationNotActive } => "cancelled",
        { Code: AiErrorCodes.ModelConfigNotFound or AiErrorCodes.ModelConfigInvalid or AiErrorCodes.ModelConfigUnavailable
            or AiErrorCodes.ChatSessionNotFound or AiErrorCodes.ChatMessageInvalid or AiErrorCodes.ChatGenerationInProgress } => "denied",
        _ => "failed"
    };
}
