using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Domain;
using Full.NET.Modules.SerialNumbers.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;

/// <summary>管理 Host 流水号规则目录，并保持写入验证与乐观并发一致。</summary>
internal sealed class HostSerialRuleService(
    IQueryExecutor queryExecutor,
    IMultiResultQueryExecutor multiResultQueryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<SerialNumberRuleResponse>>> ListAsync(
        int page,
        int pageSize,
        string? name = null,
        string? key = null,
        bool? isEnabled = null,
        string? sortBy = null,
        string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = ((long)page - 1) * pageSize;
        var orderByClause = SerialNumberSql.ResolveRuleListOrderBy(
            sortBy,
            sortDirection);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                SerialNumberSql.CreatePageRulesSqlServer(orderByClause),
            DatabaseProvider.MySql =>
                SerialNumberSql.CreatePageRulesMySql(orderByClause),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var result = await multiResultQueryExecutor.QueryMultipleAsync(
                statement,
                new
                {
                    Offset = offset,
                    PageSize = pageSize,
                    NameContains = NormalizeContains(name),
                    KeyContains = NormalizeContains(key),
                    IsEnabled = isEnabled,
                },
                async (reader, _) =>
                {
                    var total = await reader.ReadSingleOrDefaultAsync<long>()
                        .ConfigureAwait(false);
                    var rows = await reader.ReadAsync<SerialNumberRuleRecord>()
                        .ConfigureAwait(false);
                    return (Total: total, Rows: rows);
                },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<SerialNumberRuleResponse>>.Success(
            new PagedResult<SerialNumberRuleResponse>(
                result.Rows.Select(Map).ToArray(),
                page,
                pageSize,
                result.Total));
    }

    private static string? NormalizeContains(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public async Task<Result<SerialNumberRuleResponse>> GetAsync(
        Guid ruleId,
        CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? NotFound()
            : Result<SerialNumberRuleResponse>.Success(Map(row));
    }

    public Task<Result<SerialNumberRuleResponse>> CreateAsync(
        Guid actorUserId,
        CreateSerialNumberRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = Normalize(
            request.RuleKey,
            request.DisplayName,
            request.Description,
            request.Scope,
            request.ResetInterval,
            request.Pattern,
            request.MinimumValue,
            request.MaximumValue,
            request.DisplayOrder,
            request.IsEnabled);
        if (!input.IsSuccess)
        {
            return Task.FromResult(
                Result<SerialNumberRuleResponse>.Failure(input.Error!));
        }

        return CreateInTransactionAsync(
            actorUserId,
            input.Value!,
            cancellationToken);
    }

    public Task<Result<SerialNumberRuleResponse>> UpdateAsync(
        Guid ruleId,
        Guid actorUserId,
        UpdateSerialNumberRuleRequest request,
        CancellationToken cancellationToken = default)
    {
        var input = Normalize(
            null,
            request.DisplayName,
            request.Description,
            request.Scope,
            request.ResetInterval,
            request.Pattern,
            request.MinimumValue,
            request.MaximumValue,
            request.DisplayOrder,
            request.IsEnabled);
        if (!input.IsSuccess || request.Version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteAsync(
            token => UpdateCoreAsync(
                ruleId,
                actorUserId,
                request.Version,
                input.Value!,
                token),
            cancellationToken);
    }

    public Task<Result<SerialNumberRuleResponse>> SetEnabledAsync(
        Guid ruleId,
        Guid actorUserId,
        long version,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        if (version < 1)
        {
            return Task.FromResult(Invalid());
        }

        return transaction.ExecuteAsync(
            token => SetEnabledCoreAsync(
                ruleId,
                actorUserId,
                version,
                isEnabled,
                token),
            cancellationToken);
    }

    private async Task<Result<SerialNumberRuleResponse>>
        CreateInTransactionAsync(
            Guid actorUserId,
            NormalizedRule input,
            CancellationToken cancellationToken)
    {
        try
        {
            return await transaction.ExecuteAsync(
                    async token =>
                    {
                        var now = clock.UtcNow;
                        var id = idGenerator.NewId();
                        await commandExecutor.ExecuteAsync(
                                SerialNumberSql.InsertRule,
                                new
                                {
                                    Id = id,
                                    input.RuleKey,
                                    input.DisplayName,
                                    input.Description,
                                    Scope = (int)input.Scope,
                                    ResetInterval = (int)input.ResetInterval,
                                    input.Pattern,
                                    input.MinimumValue,
                                    input.MaximumValue,
                                    input.DisplayOrder,
                                    input.IsEnabled,
                                    CreatedAtUtc = now,
                                    CreatedByUserId = actorUserId,
                                },
                                token)
                            .ConfigureAwait(false);
                        return Result<SerialNumberRuleResponse>.Success(
                            new SerialNumberRuleResponse(
                                id,
                                input.RuleKey!,
                                input.DisplayName,
                                input.Description,
                                input.Scope,
                                input.ResetInterval,
                                input.Pattern,
                                input.MinimumValue,
                                input.MaximumValue,
                                input.DisplayOrder,
                                input.IsEnabled,
                                now,
                                actorUserId,
                                null,
                                null,
                                1));
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DataCommandException exception)
            when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            return Result<SerialNumberRuleResponse>.Failure(new Error(
                SerialNumberErrorCodes.RuleKeyExists,
                "The serial number rule key already exists.",
                ErrorType.Conflict));
        }
    }

    private async Task<Result<SerialNumberRuleResponse>> UpdateCoreAsync(
        Guid ruleId,
        Guid actorUserId,
        long version,
        NormalizedRule input,
        CancellationToken cancellationToken)
    {
        var existing = await FindForMutationAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.Version != version)
        {
            return VersionConflict();
        }

        if (HasAllocationSemanticsChanged(existing, input))
        {
            var allocationCount = await queryExecutor
                .QuerySingleOrDefaultAsync<long>(
                    SerialNumberSql.CountAllocationsByRule,
                    new { RuleId = ruleId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (allocationCount > 0)
            {
                return SemanticsLocked();
            }
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                SerialNumberSql.UpdateRule,
                new
                {
                    Id = ruleId,
                    input.DisplayName,
                    input.Description,
                    Scope = (int)input.Scope,
                    ResetInterval = (int)input.ResetInterval,
                    input.Pattern,
                    input.MinimumValue,
                    input.MaximumValue,
                    input.DisplayOrder,
                    input.IsEnabled,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        return affected == 1
            ? Result<SerialNumberRuleResponse>.Success(new SerialNumberRuleResponse(
                ruleId,
                existing.RuleKey,
                input.DisplayName,
                input.Description,
                input.Scope,
                input.ResetInterval,
                input.Pattern,
                input.MinimumValue,
                input.MaximumValue,
                input.DisplayOrder,
                input.IsEnabled,
                existing.CreatedAtUtc,
                existing.CreatedByUserId,
                now,
                actorUserId,
                version + 1))
            : VersionConflict();
    }

    private async Task<Result<SerialNumberRuleResponse>> SetEnabledCoreAsync(
        Guid ruleId,
        Guid actorUserId,
        long version,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var existing = await FindForMutationAsync(ruleId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
                SerialNumberSql.SetRuleEnabled,
                new
                {
                    Id = ruleId,
                    IsEnabled = isEnabled,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = actorUserId,
                    Version = version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return VersionConflict();
        }

        return Result<SerialNumberRuleResponse>.Success(Map(existing) with
        {
            IsEnabled = isEnabled,
            UpdatedAtUtc = now,
            UpdatedByUserId = actorUserId,
            Version = version + 1,
        });
    }

    private Task<SerialNumberRuleRecord?> FindAsync(
        Guid ruleId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<SerialNumberRuleRecord>(
            SerialNumberSql.FindRuleById,
            new { Id = ruleId },
            cancellationToken);

    private Task<SerialNumberRuleRecord?> FindForMutationAsync(
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                SerialNumberSql.LockRuleForMutationSqlServer,
            DatabaseProvider.MySql =>
                SerialNumberSql.LockRuleForMutationMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        return queryExecutor.QuerySingleOrDefaultAsync<SerialNumberRuleRecord>(
            statement,
            new { Id = ruleId },
            cancellationToken);
    }

    private static bool HasAllocationSemanticsChanged(
        SerialNumberRuleRecord existing,
        NormalizedRule input) =>
        existing.Scope != (int)input.Scope
        || existing.ResetInterval != (int)input.ResetInterval
        || !string.Equals(existing.Pattern, input.Pattern, StringComparison.Ordinal)
        || existing.MinimumValue != input.MinimumValue
        || existing.MaximumValue != input.MaximumValue;

    private static Result<NormalizedRule> Normalize(
        string? ruleKey,
        string? displayName,
        string? description,
        SerialNumberRuleScope scope,
        SerialNumberResetInterval resetInterval,
        string? pattern,
        long minimumValue,
        long maximumValue,
        int displayOrder,
        bool isEnabled)
    {
        var normalizedRuleKey = ruleKey?.Trim();
        var normalizedDisplayName = displayName?.Trim() ?? string.Empty;
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        var parsed = SerialNumberPattern.Parse(pattern, scope);
        if ((ruleKey is not null && !IsRuleKey(normalizedRuleKey))
            || normalizedDisplayName.Length is < 1 or > 128
            || normalizedDescription is { Length: > 512 }
            || !Enum.IsDefined(scope)
            || !Enum.IsDefined(resetInterval)
            || pattern is null
            || !parsed.IsSuccess
            || minimumValue < 1
            || maximumValue < minimumValue
            || maximumValue > parsed.Value?.MaximumSequenceValue)
        {
            return Result<NormalizedRule>.Failure(InvalidError());
        }

        return Result<NormalizedRule>.Success(new NormalizedRule(
            normalizedRuleKey,
            normalizedDisplayName,
            normalizedDescription,
            scope,
            resetInterval,
            pattern,
            minimumValue,
            maximumValue,
            displayOrder,
            isEnabled));
    }

    private static bool IsRuleKey(string? value)
    {
        if (value is not { Length: >= 1 and <= 128 }
            || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        return value.All(character =>
            character is >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.'
            or '_'
            or '-');
    }

    internal static SerialNumberRuleResponse Map(
        SerialNumberRuleRecord row) =>
        new(
            row.Id,
            row.RuleKey,
            row.DisplayName,
            row.Description,
            (SerialNumberRuleScope)row.Scope,
            (SerialNumberResetInterval)row.ResetInterval,
            row.Pattern,
            row.MinimumValue,
            row.MaximumValue,
            row.DisplayOrder,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.UpdatedAtUtc,
            row.UpdatedByUserId,
            row.Version);

    private static Result<SerialNumberRuleResponse> Invalid() =>
        Result<SerialNumberRuleResponse>.Failure(InvalidError());

    private static Error InvalidError() => new(
        SerialNumberErrorCodes.RuleInvalid,
        "The serial number rule is invalid.",
        ErrorType.Validation);

    private static Result<SerialNumberRuleResponse> NotFound() =>
        Result<SerialNumberRuleResponse>.Failure(new Error(
            SerialNumberErrorCodes.RuleNotFound,
            "The serial number rule was not found.",
            ErrorType.NotFound));

    private static Result<SerialNumberRuleResponse> VersionConflict() =>
        Result<SerialNumberRuleResponse>.Failure(new Error(
            SerialNumberErrorCodes.RuleVersionConflict,
            "The serial number rule was updated concurrently.",
            ErrorType.Conflict));

    private static Result<SerialNumberRuleResponse> SemanticsLocked() =>
        Result<SerialNumberRuleResponse>.Failure(new Error(
            SerialNumberErrorCodes.RuleSemanticsLocked,
            "The serial number allocation semantics are locked.",
            ErrorType.Conflict));

    private sealed record NormalizedRule(
        string? RuleKey,
        string DisplayName,
        string? Description,
        SerialNumberRuleScope Scope,
        SerialNumberResetInterval ResetInterval,
        string Pattern,
        long MinimumValue,
        long MaximumValue,
        int DisplayOrder,
        bool IsEnabled);
}
