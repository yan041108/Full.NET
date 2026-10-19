using Full.NET.Modules.Ai.Diagnostics;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>每个候选独立恢复租户、竞争租约并调用原会话消费者。</summary>
internal sealed class AiKnowledgeIndexWorkerCoordinator(IQueryExecutor query, IDataTransactionState transactions,
    IServiceScopeFactory scopes, IClock clock, IOptions<DatabaseOptions> database, IOptions<AiKnowledgeIndexWorkerOptions> options,
    ILogger<AiKnowledgeIndexWorkerCoordinator> logger, IOptions<AiKnowledgePdfRuntimeOptions>? pdfOptions = null,
    IOptions<AiKnowledgeDocxRuntimeOptions>? docxOptions = null)
{
    internal async Task<int> ProcessPendingAsync(CancellationToken token)
    {
        Guard(token);
        var configuration = options.Value;
        if (configuration.BatchSize is < 1 or > 32 || configuration.PollMilliseconds is < 250 or > 60000)
            throw new InvalidOperationException("Knowledge worker configuration is outside its bounds.");
        if (!configuration.Enabled) return 0;
        var pdf = pdfOptions?.Value ?? new AiKnowledgePdfRuntimeOptions();
        var docx = docxOptions?.Value ?? new AiKnowledgeDocxRuntimeOptions();
        var statement = database.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AiKnowledgeIndexWorkerSql.ScanSqlServer,
            DatabaseProvider.MySql => AiKnowledgeIndexWorkerSql.ScanMySql,
            _ => throw new InvalidOperationException("Unsupported knowledge worker provider."),
        };
        var rows = await query.QueryAsync<AiKnowledgeIndexWorkerRecord>(statement,
            new Dictionary<string, object?> { ["BatchSize"] = configuration.BatchSize, ["Now"] = clock.UtcNow, ["PdfEnabled"] = pdf.Enabled ? 1 : 0, ["DocxEnabled"] = docx.Enabled ? 1 : 0 }, token).ConfigureAwait(false);
        Guard(token);
        if (rows.Count > configuration.BatchSize) throw new InvalidOperationException("Knowledge scan exceeded its bound.");
        var acquired = 0;
        foreach (var row in rows)
        {
            Guard(token);
            if (row.TaskId == Guid.Empty || row.KnowledgeBaseId == Guid.Empty || row.DocumentId == Guid.Empty || row.VersionId == Guid.Empty
                || row.OwnerUserId == Guid.Empty || row.TenantId == Guid.Empty || row.DocumentVersion <= 0 || row.KnowledgeBaseVersion <= 0
                || !(pdf.Supports(row.ContentType) || docx.Enabled && row.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType)) continue;
            // 候选来自固定服务端扫描；每个任务重新创建上下文，不能污染父级或复用另一租户的 DbSession。
            await using var scope = scopes.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var tenant = provider.GetRequiredService<ICurrentTenantContextWriter>();
            if (row.TenantId is { } tenantId) tenant.SetTenant(new(tenantId, tenantId.ToString("N"), tenantId.ToString("N")));
            else tenant.SetHost();
            var childTransactions = provider.GetRequiredService<IDataTransactionState>();
            Guard(token, childTransactions);
            var tasks = provider.GetRequiredService<AiKnowledgeIndexTaskStore>();
            var binding = new AiKnowledgeIndexTaskBinding(row.KnowledgeBaseId, row.DocumentId, row.VersionId, row.DocumentVersion, row.KnowledgeBaseVersion, row.OwnerUserId);
            var held = await tasks.AcquireAsync(binding, row.TaskId, token).ConfigureAwait(false);
            Guard(token, childTransactions);
            if (held?.LeaseToken is not { } leaseToken) continue;
            acquired++;
            // 同一任务作用域只接收消费者已经验证的快照，内层退出后仍覆盖本层失败记录。
            using var correlation = provider.GetRequiredService<AiBackgroundLogCorrelation>().BeginCapture();
            try
            {
                // 格式只决定消费者；消费者仍从当前权威来源复核，扫描行不授予模型派发资格。
                var completed = row.ContentType == "application/pdf"
                    ? await provider.GetRequiredService<AiKnowledgeIndexPdfPersistenceService>().ExecuteGenerationAsync(binding, row.TaskId, leaseToken, token).ConfigureAwait(false)
                    : row.ContentType == Domain.AiKnowledgeFilePolicy.DocxContentType
                    ? await provider.GetRequiredService<AiKnowledgeIndexDocxPersistenceService>().ExecuteGenerationAsync(binding, row.TaskId, leaseToken, token).ConfigureAwait(false)
                    : await provider.GetRequiredService<AiKnowledgeIndexPersistenceService>().ExecuteTextGenerationAsync(binding, row.TaskId, leaseToken, token).ConfigureAwait(false);
                Guard(token, childTransactions);
                if (!completed) await FailAsync(tasks, binding, row.TaskId, leaseToken, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 关闭时保留持租约状态；已派发费用由原消费者独立结算，不免费重发缺失向量。
                throw;
            }
            catch (Exception exception)
            {
                Guard(token, childTransactions);
                await FailAsync(tasks, binding, row.TaskId, leaseToken, token).ConfigureAwait(false);
                // 只记录固定类别与稳定码，不把异常类型名、正文、SQL 或凭据写入日志。
                AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIndex, exception, row.TaskId);
            }
        }
        return acquired;
    }

    private static async Task FailAsync(AiKnowledgeIndexTaskStore tasks, AiKnowledgeIndexTaskBinding binding, Guid taskId, Guid leaseToken, CancellationToken token)
    {
        // 拒绝原因不能由消费者返回的 false 猜测；旧租约或旧来源不得写回覆盖新的代次。
        await tasks.FailAsync(binding, taskId, leaseToken, AiKnowledgeIndexFailure.IndexUnavailable, token).ConfigureAwait(false);
    }

    private void Guard(CancellationToken token, IDataTransactionState? child = null)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction || child?.HasTransaction == true) throw new InvalidOperationException("Knowledge worker cannot run inside a transaction.");
    }
}
