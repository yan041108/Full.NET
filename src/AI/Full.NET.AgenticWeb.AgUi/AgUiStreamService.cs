using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Agents.AgUi;
using Full.NET.AgenticWeb.AgUi.Serialization;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.AspNetCore.Http;

namespace Full.NET.AgenticWeb.AgUi;

/// <summary>从持久事件重放 AG-UI SSE；断开连接不会触发工具或模型重跑。</summary>
public sealed class AgUiStreamService(IAgentRunAgUiReader reader)
{
    public const int DefaultBatchSize = 100;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>按归属重放指定 Agent 运行的持久化事件到 SSE 响应；从 afterSequence 起流式输出，运行终止时发送终止边界，不触发工具或模型重跑。</summary>
    /// <param name="runId">Agent 运行标识。</param>
    /// <param name="scopeKey">租户或宿主演化出的作用域键。</param>
    /// <param name="actorUserId">当前调用用户标识，用于校验事件归属。</param>
    /// <param name="afterSequence">起始事件序号；0 或负数表示从运行开始重放。</param>
    /// <param name="httpContext">用于写入 text/event-stream 响应的 HTTP 上下文。</param>
    /// <param name="cancellationToken">调用方取消令牌。</param>
    /// <returns>流是否成功建立并完成输出；失败时携带错误码与类型。</returns>
    public async Task<Result<bool>> StreamOwnedRunAsync(
        Guid runId,
        string scopeKey,
        Guid actorUserId,
        long afterSequence,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await reader.TryGetOwnedSnapshotAsync(runId, scopeKey, actorUserId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return Failure(AiErrorCodes.AgentRunNotFound, ErrorType.NotFound);
        }

        if (afterSequence > snapshot.MaxEventSequence)
        {
            return Failure(AiErrorCodes.AgentRunEventsCursorExpired, ErrorType.Conflict);
        }

        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";
        httpContext.Response.ContentType = "text/event-stream";
        await httpContext.Response.StartAsync(cancellationToken).ConfigureAwait(false);

        var responseBody = httpContext.Response.Body;
        var progress = await reader.TryGetProgressAsync(runId, scopeKey, cancellationToken).ConfigureAwait(false);
        var lastSentSequence = afterSequence;

        if (afterSequence <= 0)
        {
            await WriteMappedAsync(responseBody, AgUiEventMapper.CreateRunStarted(snapshot), cancellationToken)
                .ConfigureAwait(false);
            await WriteMappedAsync(responseBody, AgUiEventMapper.CreateStateSnapshot(snapshot, progress), cancellationToken)
                .ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested && !httpContext.RequestAborted.IsCancellationRequested)
        {
            snapshot = await reader.TryGetOwnedSnapshotAsync(runId, scopeKey, actorUserId, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                return Failure(AiErrorCodes.AgentRunNotFound, ErrorType.NotFound);
            }

            if (afterSequence > snapshot.MaxEventSequence)
            {
                return Failure(AiErrorCodes.AgentRunEventsCursorExpired, ErrorType.Conflict);
            }

            progress = await reader.TryGetProgressAsync(runId, scopeKey, cancellationToken).ConfigureAwait(false);
            var events = await reader.ListEventsAfterAsync(runId, lastSentSequence, DefaultBatchSize, cancellationToken)
                .ConfigureAwait(false);
            foreach (var persisted in events)
            {
                foreach (var mapped in AgUiEventMapper.MapPersistedEvent(snapshot, persisted))
                {
                    await WriteMappedAsync(responseBody, mapped, cancellationToken).ConfigureAwait(false);
                }

                lastSentSequence = persisted.Sequence;
            }

            if (IsTerminal(snapshot.StatusKey))
            {
                if (events.Count == 0)
                {
                    var boundary = AgUiEventMapper.CreateTerminalBoundary(snapshot, includeWhenNoPersistedEvent: true);
                    if (boundary is not null)
                    {
                        await WriteMappedAsync(responseBody, boundary, cancellationToken).ConfigureAwait(false);
                    }
                }

                await WriteMappedAsync(
                    responseBody,
                    AgUiEventMapper.CreateStateSnapshot(snapshot, progress),
                    cancellationToken).ConfigureAwait(false);
                return Result<bool>.Success(true);
            }

            if (events.Count == 0)
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            await WriteMappedAsync(
                responseBody,
                AgUiEventMapper.CreateStateSnapshot(snapshot, progress),
                cancellationToken).ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    private static Result<bool> Failure(string code, ErrorType type) =>
        Result<bool>.Failure(new Error(code, "Agent run event stream rejected.", type));

    private static bool IsTerminal(string statusKey) =>
        statusKey is "completed" or "failed" or "cancelled" or "expired";

    private static async Task WriteMappedAsync(
        Stream responseBody,
        AgUiMappedEvent mapped,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(mapped.Payload, mapped.TypeInfo);
        await AgUiSseWriter.WriteRawAsync(responseBody, mapped.EventType, json, cancellationToken)
            .ConfigureAwait(false);
    }
}
