using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>持久化批次操作身份，唯一冲突不得改变既有请求或降级其他数据库错误。</summary>
internal sealed class AiKnowledgeIndexBatchStore(IQueryExecutor query, ICommandExecutor command, AiKnowledgeIndexTaskStore tasks, IIdGenerator ids)
{
    /// <summary>有效租约下建立或复用精确批次身份；批准失效或指纹冲突返回空，不覆盖已有行。</summary>
    internal async Task<AiKnowledgeIndexBatchRecord?> PrepareAsync(AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken,
        int batchOrdinal, string requestHash, int inputCount, CancellationToken token)
    {
        if (batchOrdinal is < 0 or > 4095 || inputCount is < 1 or > 16 || requestHash is null
            || requestHash.Length != 64 || requestHash.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Invalid knowledge index batch.");
        var parameters = tasks.HeldParameters(binding, taskId, leaseToken, token);
        parameters["OperationId"] = ids.NewId(); parameters["BatchOrdinal"] = batchOrdinal;
        parameters["RequestHash"] = requestHash; parameters["InputCount"] = inputCount;
        try
        {
            if (await command.ExecuteAsync(tasks.IsTenant ? AiKnowledgeIndexBatchSql.PrepareTenant : AiKnowledgeIndexBatchSql.PrepareHost, parameters, token).ConfigureAwait(false) != 1)
                return null;
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            // 只按当前精确任务和批次恢复；预算操作 ID 不能随接替租约重新生成。
        }
        var read = tasks.HeldParameters(binding, taskId, leaseToken, token); read["BatchOrdinal"] = batchOrdinal;
        var row = await query.QuerySingleOrDefaultAsync<AiKnowledgeIndexBatchRecord>(tasks.IsTenant ? AiKnowledgeIndexBatchSql.FindTenant : AiKnowledgeIndexBatchSql.FindHost, read, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return row is not null && row.Id != Guid.Empty && row.IndexTaskId == taskId && row.BatchOrdinal == batchOrdinal
            && row.InputCount == inputCount && string.Equals(row.RequestHash, requestHash, StringComparison.Ordinal) ? row : null;
    }
}
