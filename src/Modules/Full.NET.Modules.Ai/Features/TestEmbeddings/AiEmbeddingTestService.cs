using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Budgets;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.Extensions.AI;

namespace Full.NET.Modules.Ai.Features.TestEmbeddings;

/// <summary>受权管理员 Embedding 能力测试；不返回完整向量。</summary>
internal sealed class AiEmbeddingTestService(
    IQueryExecutor queryExecutor,
    IEnumerable<IAiModelClientFactory> factories,
    AiModelBindingScope bindings,
    IAiOperationBudgetStore budgetStore,
    IIdGenerator idGenerator)
{
    /// <summary>对单条或批量输入执行 Embedding 测试并结算预算。</summary>
    public async Task<Result<TestAiModelEmbeddingResult>> TestAsync(
        Guid modelConfigId,
        TestAiModelEmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(request);
        // 管理输入先冻结，数据库等待和外部集合回调不能改变指纹或放大复制成本。
        var (inputs, inputError) = ResolveInputs(request, cancellationToken);
        if (inputError is not null) return InvalidInput(inputError);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", modelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (row is null)
        {
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                AiErrorCodes.ModelConfigNotFound,
                "The AI model configuration was not found.",
                ErrorType.NotFound));
        }

        if (!row.IsEnabled)
        {
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                AiErrorCodes.ModelConfigUnavailable,
                "The AI model configuration is disabled.",
                ErrorType.BusinessRule));
        }

        var validation = AiKnowledgeEmbeddingInputPolicy.Validate(row, inputs, cancellationToken);
        if (validation is not null)
        {
            return InvalidInput(validation);
        }

        var factory = factories.FirstOrDefault(item => item.ProviderKey == row.ProviderKey);
        if (factory is null)
        {
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                AiErrorCodes.ModelConfigUnavailable,
                "The configured AI provider is not available.",
                ErrorType.BusinessRule));
        }

        var operationId = idGenerator.NewId();
        var operation = AiEmbeddingBudgetRequest.Create(operationId, row, inputs);
        AiOperationReservation? reservation = null;
        try
        {
            reservation = await budgetStore.ReserveAsync(
                operation,
                cancellationToken).ConfigureAwait(false);
            if (!reservation.IsNew || reservation.OperationId != operationId)
                throw new AiBudgetException("ai.budget.operation_conflict");
        }
        catch (AiBudgetException error)
        {
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                error.Code,
                "The AI operation budget cannot cover this request.",
                error.Code == "ai.budget.operation_conflict" ? ErrorType.Conflict : ErrorType.Forbidden));
        }

        var outcome = "failed";
        // 新预留只属于本次操作；实际进入 GenerateAsync 后才将未报告消费标为未知。
        var usage = new AiOperationUsage(0, 0);
        int? inputTokens = null;
        int dimensions = 0;
        string message = "Embedding test failed.";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var binding = bindings.Create(row);
            using (var generator = await factory.CreateEmbeddingGeneratorAsync(binding, cancellationToken).ConfigureAwait(false))
            {
                if (generator is null) throw new InvalidDataException("AI provider returned no embedding generator.");
                cancellationToken.ThrowIfCancellationRequested(); usage = new(null, null);
                var result = await generator.GenerateAsync(inputs, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (result is null) throw new InvalidDataException("AI provider returned no embeddings.");
                var reported = AiModelUsagePolicy.Read(result.Usage, embedding: true);
                // 有效计量先成为结算事实；坏向量、超出预留或客户端释放失败不能丢失已发生消费。
                usage = reported;
                cancellationToken.ThrowIfCancellationRequested();
                inputTokens = reported.InputTokens is { } tokens ? checked((int)tokens) : null;
                if (reported.InputTokens > operation.InputTokenLimit || reported.OutputTokens > 0)
                    throw new InvalidDataException("AI provider exceeded the embedding reservation.");
                if (result.Count != inputs.Count)
                    throw new InvalidDataException("AI provider returned an unexpected embedding count.");
                dimensions = result[0].Vector.Length;
                if (dimensions == 0) throw new InvalidDataException("AI provider returned empty embedding dimensions.");
                foreach (var embedding in result)
                {
                    if (embedding.Vector.Length != dimensions)
                        throw new InvalidDataException("AI provider returned inconsistent embedding dimensions.");
                    foreach (var value in embedding.Vector.Span)
                        if (!float.IsFinite(value)) throw new InvalidDataException("AI provider returned a non-finite embedding.");
                }
            }
            // 释放也属于本次调用的失败边界；只有释放完成后才记录 succeeded。
            cancellationToken.ThrowIfCancellationRequested();
            outcome = "succeeded";
            message = "Embedding test succeeded.";
            return Result<TestAiModelEmbeddingResult>.Success(
                new(true, message, dimensions, inputs.Count, inputTokens));
        }
        catch (NotSupportedException)
        {
            message = "The configured model does not support embeddings.";
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                AiErrorCodes.ModelConfigUnavailable,
                message,
                ErrorType.BusinessRule));
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            message = "Embedding test was cancelled.";
            throw;
        }
        catch (Exception)
        {
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                AiErrorCodes.ChatGenerationFailed,
                message,
                ErrorType.BusinessRule));
        }
        finally
        {
            if (reservation is not null)
            {
                await budgetStore.SettleAsync(
                    reservation.OperationId,
                    usage,
                    outcome,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>读取一次批量Count和有限索引，先限制原始字符再Trim；空批量沿用单条输入优先级。</summary>
    private static (IReadOnlyList<string> Inputs, string? Error) ResolveInputs(TestAiModelEmbeddingRequest request, CancellationToken token)
    {
        var batch = request.BatchInputs; var count = batch?.Count ?? 0; token.ThrowIfCancellationRequested();
        if (count < 0 || count > AiEmbeddingContentPolicy.MaxBatchSize)
            return ([], $"Embedding batch must not exceed {AiEmbeddingContentPolicy.MaxBatchSize} inputs.");
        var values = new string[count > 0 ? count : 1]; var total = 0;
        for (var i = 0; i < values.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var text = count > 0 ? batch![i] : request.Input; token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) return ([], "Embedding input cannot be empty.");
            if (text.Length > AiEmbeddingContentPolicy.MaxInputLength)
                return ([], $"Each embedding input must not exceed {AiEmbeddingContentPolicy.MaxInputLength} characters.");
            total += text.Length;
            if (total > AiEmbeddingContentPolicy.MaxTotalCharacters)
                return ([], $"Embedding inputs must not exceed {AiEmbeddingContentPolicy.MaxTotalCharacters} total characters.");
            values[i] = text.Trim();
        }
        return (Array.AsReadOnly(values), null);
    }
    private static Result<TestAiModelEmbeddingResult> InvalidInput(string message) => Result<TestAiModelEmbeddingResult>.Failure(
        new Error(AiErrorCodes.ModelConfigInvalid, message, ErrorType.Validation));
}
