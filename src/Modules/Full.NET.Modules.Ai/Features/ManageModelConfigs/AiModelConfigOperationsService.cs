using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Connectivity;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageModelConfigs;

/// <summary>AI 模型配置连通性测试。</summary>
internal sealed class AiModelConfigOperationsService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    AiModelConnectivityTester connectivityTester,
    IClock clock)
{
    /// <summary>测试已保存模型配置的连通性。</summary>
    /// <param name="modelConfigId">配置标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果并持久化最近测试摘要。</returns>
    public async Task<Result<TestAiModelConfigResult>> TestConnectivityAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", modelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Result<TestAiModelConfigResult>.Failure(new Error(
                AiErrorCodes.ModelConfigNotFound,
                "The AI model configuration was not found.",
                ErrorType.NotFound));
        }

        var outcome = await connectivityTester
            .TestAsync(row, cancellationToken)
            .ConfigureAwait(false);
        var statusKey = outcome.Succeeded
            ? AiModelTestStatusKeys.Succeeded
            : AiModelTestStatusKeys.Failed;
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                AiModelConfigSql.UpdateTestResult,
                AiSqlParameters.Create(
                    ("ModelConfigId", modelConfigId),
                    ("LastTestedAtUtc", now),
                    ("LastTestStatusKey", statusKey),
                    ("LastTestMessage", outcome.Message),
                    ("UpdatedAtUtc", now),
                    ("Version", row.Version)),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<TestAiModelConfigResult>.Success(
            new TestAiModelConfigResult(outcome.Succeeded, outcome.Message));
    }
}
