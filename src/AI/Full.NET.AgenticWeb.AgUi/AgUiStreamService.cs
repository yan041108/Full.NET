using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Agents.AgUi;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Full.NET.AgenticWeb.AgUi;

/// <summary>只读重放持久事件；每条连接独立开始，在完整快照之后仅发送一次最终边界。</summary>
public sealed class AgUiStreamService(IAgentRunAgUiReader reader, ILogger<AgUiStreamService>? logger = null)
{
    /// <summary>持久事件重放的默认批大小。</summary>
    public const int DefaultBatchSize = 100;
    /// <summary>空闲时的轮询间隔，控制重放跟随的实时性。</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan FrameWriteTimeout = TimeSpan.FromSeconds(10);

    /// <summary>按原请求当前资格重放既有运行；游标非负，重连不会创建或恢复业务运行。</summary>
    /// <returns>成功且已发送终态边界时返回 <see langword="true"/>；客户端断开或写入超时等取消场景返回 <see langword="false"/>；
    /// 资格失效、游标过期或未预期错误返回对应失败。</returns>
    public async Task<Result<bool>> StreamOwnedRunAsync(Guid runId, string scopeKey, Guid actorUserId,
        long afterSequence, HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, httpContext.RequestAborted);
        var token = request.Token;
        var protocolStarted = false;
        var terminalSent = false;
        var lastSent = afterSequence;
        AgUiMappedEvent? pendingBoundary = null;
        string? pendingKind = null;
        try
        {
            await AdmitAsync().ConfigureAwait(false);
            var snapshot = await reader.TryGetOwnedSnapshotAsync(runId, scopeKey, actorUserId, token).ConfigureAwait(false);
            if (snapshot is null) return Failure(AiErrorCodes.AgentRunNotFound, ErrorType.NotFound);
            if (afterSequence < 0 || afterSequence > snapshot.MaxEventSequence)
                return Failure(AiErrorCodes.AgentRunEventsCursorExpired, ErrorType.Conflict);
            // 首批在发送响应头前核对，缺失历史不能被一个当前快照悄悄替代。
            IReadOnlyList<AgentRunPersistedEvent>? page = await reader.ListEventsAfterAsync(runId, afterSequence, DefaultBatchSize, token).ConfigureAwait(false);
            ValidatePage(page, lastSent, snapshot.MaxEventSequence);
            var progress = await reader.TryGetProgressAsync(runId, scopeKey, token).ConfigureAwait(false);
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.ContentType = "text/event-stream";
            // 每条标准客户端连接都需要自己的开始帧，包含afterSequence的只读重连也一样。
            await WriteAsync(AgUiEventMapper.CreateRunStarted(snapshot)).ConfigureAwait(false);
            protocolStarted = true;
            await WriteAsync(AgUiEventMapper.CreateStateSnapshot(snapshot, progress)).ConfigureAwait(false);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                await AdmitAsync().ConfigureAwait(false);
                snapshot = await reader.TryGetOwnedSnapshotAsync(runId, scopeKey, actorUserId, token).ConfigureAwait(false);
                if (snapshot is null) throw new AdmissionDenied();
                page ??= await reader.ListEventsAfterAsync(runId, lastSent, DefaultBatchSize, token).ConfigureAwait(false);
                ValidatePage(page, lastSent, snapshot.MaxEventSequence);
                foreach (var persisted in page)
                {
                    var hasCustom = false;
                    foreach (var mapped in AgUiEventMapper.MapPersistedEvent(snapshot, persisted))
                    {
                        if (mapped.EventType is AgUiProtocolEventTypes.RunFinished or AgUiProtocolEventTypes.RunError)
                        {
                            pendingBoundary = mapped;
                            pendingKind = persisted.EventType;
                            if (!hasCustom)
                                await WriteAsync(AgUiEventMapper.CreateHistoricalBoundary(snapshot, persisted, mapped)).ConfigureAwait(false);
                        }
                        else
                        {
                            hasCustom |= mapped.EventType == AgUiProtocolEventTypes.Custom;
                            await WriteAsync(mapped).ConfigureAwait(false);
                        }
                    }
                    lastSent = persisted.Sequence;
                }
                // 查询与输出期间状态可变化；交付最终快照前刷新，历史中断仅作为有游标的诊断事件。
                await AdmitAsync().ConfigureAwait(false);
                snapshot = await reader.TryGetOwnedSnapshotAsync(runId, scopeKey, actorUserId, token).ConfigureAwait(false);
                if (snapshot is null) throw new AdmissionDenied();
                if (page.Count > 0 || IsQuiescent(snapshot.StatusKey))
                    progress = await reader.TryGetProgressAsync(runId, scopeKey, token).ConfigureAwait(false);
                if (IsQuiescent(snapshot.StatusKey) && lastSent >= snapshot.MaxEventSequence)
                {
                    await WriteAsync(AgUiEventMapper.CreateStateSnapshot(snapshot, progress)).ConfigureAwait(false);
                    var boundary = pendingKind == "run." + snapshot.StatusKey
                        && snapshot.StatusKey is "completed" or "failed" or "cancelled" or "awaiting_approval"
                        ? pendingBoundary : AgUiEventMapper.CreateTerminalBoundary(snapshot, true);
                    if (boundary is not null)
                    {
                        await WriteAsync(boundary).ConfigureAwait(false);
                        terminalSent = true;
                    }
                    return Result<bool>.Success(true);
                }
                if (page.Count > 0)
                    await WriteAsync(AgUiEventMapper.CreateStateSnapshot(snapshot, progress)).ConfigureAwait(false);
                else
                    await Task.Delay(PollInterval, token).ConfigureAwait(false);
                page = null;
            }
        }
        catch (AdmissionDenied)
        {
            await RejectStartedAsync("authorization.permission_denied").ConfigureAwait(false);
            return Failure("authorization.permission_denied", ErrorType.Forbidden);
        }
        catch (CursorGap)
        {
            await RejectStartedAsync(AiErrorCodes.AgentRunEventsCursorExpired).ConfigureAwait(false);
            return Failure(AiErrorCodes.AgentRunEventsCursorExpired, ErrorType.Conflict);
        }
        catch (OperationCanceledException)
        {
            // 断开或慢消费者写入超时只结束读取，不改变原运行和任何资金状态。
            return Result<bool>.Success(false);
        }
        catch (Exception)
        {
            try { logger?.LogWarning("Agent event stream unavailable for run {RunId}.", runId); } catch (Exception) { }
            await RejectStartedAsync("common.unexpected").ConfigureAwait(false);
            return Failure("common.unexpected", ErrorType.Unexpected);
        }

        async ValueTask AdmitAsync()
        {
            token.ThrowIfCancellationRequested();
            if (!await reader.CanReadAsync(runId, scopeKey, actorUserId, token).ConfigureAwait(false))
                throw new AdmissionDenied();
            token.ThrowIfCancellationRequested();
        }
        async Task WriteAsync(AgUiMappedEvent mapped)
        {
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            window.CancelAfter(FrameWriteTimeout);
            if (!await reader.CanReadAsync(runId, scopeKey, actorUserId, window.Token).ConfigureAwait(false))
                throw new AdmissionDenied();
            window.Token.ThrowIfCancellationRequested();
            await AgUiSseWriter.WriteRawAsync(httpContext.Response.Body, mapped.EventType,
                JsonSerializer.Serialize(mapped.Payload, mapped.TypeInfo), window.Token).ConfigureAwait(false);
            window.Token.ThrowIfCancellationRequested();
        }
        async Task RejectStartedAsync(string code)
        {
            if (!protocolStarted || terminalSent || token.IsCancellationRequested) return;
            // 已失去资格只发送无私有字段的结束码，绝不映射第二个JSON响应或暴露异常。
            using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
            window.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var failure = AgUiEventMapper.CreateReadFailure(code);
                await AgUiSseWriter.WriteRawAsync(httpContext.Response.Body, failure.EventType,
                    JsonSerializer.Serialize(failure.Payload, failure.TypeInfo), window.Token).ConfigureAwait(false);
            }
            catch (Exception) { }
        }
    }

    private static void ValidatePage(IReadOnlyList<AgentRunPersistedEvent> events, long after, long maximum)
    {
        if (events.Count > DefaultBatchSize || events.Count == 0 && maximum > after) throw new CursorGap();
        foreach (var item in events)
        {
            if (after == long.MaxValue || item.Sequence != after + 1) throw new CursorGap();
            if (item.PayloadVersion != 1 || item.Payload.Length > 65536) throw new InvalidDataException("Unsupported bounded event payload.");
            after = item.Sequence;
        }
    }
    private static bool IsQuiescent(string status) => status is "completed" or "failed" or "cancelled" or "expired"
        or "awaiting_approval" or "authorization_required" or "reconciliation_required";
    private static Result<bool> Failure(string code, ErrorType type) =>
        Result<bool>.Failure(new Error(code, "Agent run event stream rejected.", type));
    private sealed class AdmissionDenied : Exception { }
    private sealed class CursorGap : Exception { }
}
