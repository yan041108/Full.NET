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
        var row = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", modelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
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

        var inputs = ResolveInputs(request);
        var validation = AiEmbeddingContentPolicy.ValidateInputs(inputs);
        if (validation is not null)
        {
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                AiErrorCodes.ModelConfigInvalid,
                validation,
                ErrorType.Validation));
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
        AiOperationReservation? reservation = null;
        try
        {
            reservation = await budgetStore.ReserveAsync(
                AiEmbeddingBudgetRequest.Create(operationId, row, inputs),
                cancellationToken).ConfigureAwait(false);
            if (!reservation.IsNew)
                throw new AiBudgetException("ai.budget.operation_conflict");
        }
        catch (AiBudgetException error)
        {
            return Result<TestAiModelEmbeddingResult>.Failure(new Error(
                error.Code,
                "The AI operation budget cannot cover this request.",
                error.Code == "ai.budget.operation_conflict" ? ErrorType.Conflict : ErrorType.Forbidden));
        }

        var binding = bindings.Create(row);
        var outcome = "failed";
        int? inputTokens = null;
        int dimensions = 0;
        string message = "Embedding test failed.";
        try
        {
            using var generator = await factory.CreateEmbeddingGeneratorAsync(binding, cancellationToken)
                .ConfigureAwait(false);
            var result = await generator.GenerateAsync(inputs, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.Count != inputs.Count)
                throw new InvalidDataException("AI provider returned an unexpected embedding count.");

            dimensions = result[0].Vector.Length;
            foreach (var embedding in result)
            {
                if (embedding.Vector.Length != dimensions)
                    throw new InvalidDataException("AI provider returned inconsistent embedding dimensions.");
            }

            inputTokens = result.Usage?.InputTokenCount is long tokens ? (int)tokens : null;
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
                    new(inputTokens, 0),
                    outcome,
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private static IReadOnlyList<string> ResolveInputs(TestAiModelEmbeddingRequest request)
    {
        if (request.BatchInputs is { Count: > 0 })
            return request.BatchInputs.Select(item => item.Trim()).ToArray();
        if (!string.IsNullOrWhiteSpace(request.Input))
            return [request.Input.Trim()];
        return [];
    }
}
