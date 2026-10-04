using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>入队和重试绑定当前来源与批准，不使用客户端提供的租户或供应商配置。</summary>
internal sealed record AiKnowledgeIndexTaskBinding(Guid KnowledgeBaseId, Guid DocumentId, Guid VersionId,
    int DocumentVersion, int KnowledgeBaseVersion, Guid OwnerUserId);

internal enum AiKnowledgeIndexFailure { ParsingFailed, ModelUnavailable, IndexUnavailable, UsageUnknown }

/// <summary>仅负责 AI 本地持久化；领取成功不是外部派发授权或索引 ready 证明。</summary>
internal sealed class AiKnowledgeIndexTaskStore(IQueryExecutor query, ICommandExecutor command,
    ICurrentTenant tenant, IClock clock, IIdGenerator ids)
{
    private bool IsTenant => AiKnowledgeScope.IsTenant(tenant);

    internal async Task<AiKnowledgeIndexTaskRecord?> EnqueueAsync(AiKnowledgeIndexTaskBinding binding, CancellationToken token)
    {
        var parameters = Parameters(binding, token);
        parameters["TaskId"] = ids.NewId();
        try
        {
            if (await command.ExecuteAsync(IsTenant ? AiKnowledgeIndexTaskSql.EnqueueTenant : AiKnowledgeIndexTaskSql.EnqueueHost, parameters, token).ConfigureAwait(false) != 1)
                return null;
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 同一来源及批准快照的竞争只复用唯一绑定；其他故障不得降级为成功。
            return await FindBindingAsync(parameters, token).ConfigureAwait(false);
        }
        return await FindBindingAsync(parameters, token).ConfigureAwait(false);
    }

    internal Task<AiKnowledgeIndexTaskRecord?> FindAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTaskRecord>(IsTenant ? AiKnowledgeIndexTaskSql.FindTenant : AiKnowledgeIndexTaskSql.FindHost,
            TaskParameters(binding, taskId, token), token);

    internal async Task<AiKnowledgeIndexTaskRecord?> AcquireAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, CancellationToken token)
    {
        var parameters = TaskParameters(binding, taskId, token);
        var leaseToken = ids.NewId();
        parameters["LeaseToken"] = leaseToken;
        parameters["LeaseExpiresAtUtc"] = ((DateTimeOffset)parameters["Now"]!).AddSeconds(30);
        if (await command.ExecuteAsync(IsTenant ? AiKnowledgeIndexTaskSql.AcquireTenant : AiKnowledgeIndexTaskSql.AcquireHost, parameters, token).ConfigureAwait(false) != 1)
            return null;
        var row = await FindAsync(binding, taskId, token).ConfigureAwait(false);
        // 领取与读取之间可能被取消或接替；不把旧确认返回为可执行租约。
        return row is { StateKey: "running" } && row.LeaseToken == leaseToken && row.LeaseExpiresAtUtc > clock.UtcNow ? row : null;
    }

    internal Task<bool> RenewAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        var parameters = HeldParameters(binding, taskId, leaseToken, token);
        parameters["LeaseExpiresAtUtc"] = ((DateTimeOffset)parameters["Now"]!).AddSeconds(30);
        return WriteAsync(IsTenant ? AiKnowledgeIndexTaskSql.RenewTenant : AiKnowledgeIndexTaskSql.RenewHost, parameters, token);
    }

    internal Task<bool> FailAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, AiKnowledgeIndexFailure failure, CancellationToken token)
    {
        var parameters = HeldParameters(binding, taskId, leaseToken, token);
        parameters["FailureCode"] = failure switch
        {
            AiKnowledgeIndexFailure.ParsingFailed => "ai.knowledge.parsing_failed",
            AiKnowledgeIndexFailure.ModelUnavailable => "ai.knowledge.model_unavailable",
            AiKnowledgeIndexFailure.IndexUnavailable => "ai.knowledge.index_unavailable",
            AiKnowledgeIndexFailure.UsageUnknown => "ai.knowledge.usage_unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        return WriteAsync(IsTenant ? AiKnowledgeIndexTaskSql.FailTenant : AiKnowledgeIndexTaskSql.FailHost, parameters, token);
    }

    internal Task<bool> RetryAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, CancellationToken token) =>
        WriteAsync(IsTenant ? AiKnowledgeIndexTaskSql.RetryTenant : AiKnowledgeIndexTaskSql.RetryHost, TaskParameters(binding, taskId, token), token);

    internal Task<bool> CancelAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, CancellationToken token) =>
        WriteAsync(IsTenant ? AiKnowledgeIndexTaskSql.CancelTenant : AiKnowledgeIndexTaskSql.CancelHost, TaskParameters(binding, taskId, token), token);

    private Task<AiKnowledgeIndexTaskRecord?> FindBindingAsync(Dictionary<string, object?> parameters, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<AiKnowledgeIndexTaskRecord>(IsTenant ? AiKnowledgeIndexTaskSql.FindBindingTenant : AiKnowledgeIndexTaskSql.FindBindingHost, parameters, token);

    private async Task<bool> WriteAsync(SqlStatement sql, Dictionary<string, object?> parameters, CancellationToken token) =>
        await command.ExecuteAsync(sql, parameters, token).ConfigureAwait(false) == 1;

    private Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.KnowledgeBaseId == Guid.Empty || binding.DocumentId == Guid.Empty || binding.VersionId == Guid.Empty
            || binding.OwnerUserId == Guid.Empty || binding.DocumentVersion <= 0 || binding.KnowledgeBaseVersion <= 0)
            throw new ArgumentException("Invalid knowledge index binding.", nameof(binding));
        _ = IsTenant;
        return new(StringComparer.Ordinal)
        {
            ["KnowledgeBaseId"] = binding.KnowledgeBaseId, ["DocumentId"] = binding.DocumentId,
            ["VersionId"] = binding.VersionId, ["DocumentVersion"] = binding.DocumentVersion,
            ["KnowledgeBaseVersion"] = binding.KnowledgeBaseVersion, ["OwnerUserId"] = binding.OwnerUserId,
            ["Now"] = clock.UtcNow,
        };
    }

    private Dictionary<string, object?> TaskParameters(AiKnowledgeIndexTaskBinding binding, Guid taskId, CancellationToken token)
    {
        var parameters = Parameters(binding, token);
        if (taskId == Guid.Empty) throw new ArgumentException("An index task ID is required.", nameof(taskId));
        parameters["TaskId"] = taskId;
        return parameters;
    }

    private Dictionary<string, object?> HeldParameters(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        var parameters = TaskParameters(binding, taskId, token);
        if (leaseToken == Guid.Empty) throw new ArgumentException("A lease token is required.", nameof(leaseToken));
        parameters["LeaseToken"] = leaseToken;
        return parameters;
    }
}
