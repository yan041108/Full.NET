using Full.NET.Agents.Runtime;
using Full.NET.AI.Abstractions.Budgets;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>知识问答复用固定阶段观测；不读取正文、分类、身份、凭据或异常消息，也不生成业务标识。</summary>
internal static class AiKnowledgeTelemetry
{
    /// <summary>同步本地阶段只记录固定结果；不增加业务标识，不拥有输入或调用方取消源。</summary>
    internal static T Run<T>(string stage, Func<T> action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(action);
        using var observation = AgentExecutionTelemetry.Start(Guid.Empty, stage, cancellationToken: token);
        try { var result = action(); observation.Outcome = Outcome(result); return result; }
        catch (AiBudgetException) { observation.Outcome = "budget_rejected"; throw; }
        catch (UnauthorizedAccessException) { observation.Outcome = "authorization_required"; throw; }
        catch (OperationCanceledException) { observation.Outcome = "cancelled"; throw; }
    }

    /// <summary>围绕原执行边界记录结果；异常原样传播，完成不等于 HTTP 交付或持久账本成功。</summary>
    /// <param name="operationId">业务已经创建的预算操作标识；空值不输出，禁止为观测另建操作。</param>
    internal static async Task<T> RunAsync<T>(string stage, Func<Task<T>> action, CancellationToken token, Guid? operationId = null)
    {
        using var observation = AgentExecutionTelemetry.Start(Guid.Empty, stage, operationId, cancellationToken: token);
        try
        {
            var result = await action().ConfigureAwait(false);
            // 只有严格解析后的结构化拒答可分类；不检查或记录模型自然语言内容。
            observation.Outcome = Outcome(result);
            return result;
        }
        catch (AiBudgetException) { observation.Outcome = "budget_rejected"; throw; }
        catch (UnauthorizedAccessException) { observation.Outcome = "authorization_required"; throw; }
        catch (OperationCanceledException) { observation.Outcome = "cancelled"; throw; }
    }

    /// <summary>交付复核只表示原资格检查结束；不派发模型、计费或证明响应已写入客户端。</summary>
    internal static Task RunAsync(string stage, Func<Task> action, CancellationToken token, Guid? operationId = null) =>
        RunAsync(stage, async () => { await action().ConfigureAwait(false); return true; }, token, operationId);

    private static string Outcome<T>(T result) => result switch
    {
        AiKnowledgeAnswer { Refused: true } => "refused",
        AiKnowledgeExecutedAnswer { Answer.Refused: true } => "refused",
        _ => "completed"
    };
}
