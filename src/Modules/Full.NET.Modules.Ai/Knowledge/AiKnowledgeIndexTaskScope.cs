using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Ai.Features;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>索引原作用域与参数边界；完成证明不依赖可派发任务协调器，避免来源核证形成循环依赖。</summary>
internal sealed class AiKnowledgeIndexTaskScope(ICurrentTenant tenant, IClock clock)
{
    internal bool IsTenant => AiKnowledgeScope.IsTenant(tenant);
    internal Dictionary<string, object?> Parameters(AiKnowledgeIndexTaskBinding binding, CancellationToken token)
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

    internal Dictionary<string, object?> TaskParameters(AiKnowledgeIndexTaskBinding binding, Guid taskId, CancellationToken token)
    {
        var parameters = Parameters(binding, token);
        if (taskId == Guid.Empty) throw new ArgumentException("An index task ID is required.", nameof(taskId));
        parameters["TaskId"] = taskId;
        return parameters;
    }

    internal Dictionary<string, object?> HeldParameters(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        var parameters = TaskParameters(binding, taskId, token);
        if (leaseToken == Guid.Empty) throw new ArgumentException("A lease token is required.", nameof(leaseToken));
        parameters["LeaseToken"] = leaseToken;
        return parameters;
    }
}
