using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Domain;
using Full.NET.Modules.SerialNumbers.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.SerialNumbers.Features.AllocateSerialNumbers;

/// <summary>
/// 在数据库事务内原子推进计数器并持久化幂等结果，不以缓存锁承担正确性。
/// 并发安全边界：
/// 1) 计数器推进由 SerialNumberSql.Allocate* 在数据库事务内串行化——
///    SQL Server 用 sp_getapplock 命名锁，MySQL 用 ON DUPLICATE KEY UPDATE 的原子语义；
/// 2) 幂等性由 (RuleId, TenantId, IdempotencyKey) 联合唯一键保证，
///    重复请求触发 UniqueConstraint 后由 FindReplayAfterConflictAsync 查询并回放既有结果；
/// 3) 命名锁 Resource 由 CreateLockResource 拼装为 Full.NET.SerialNumbers:{RuleId}:{TenantId|host}:{ResetBucket}，
///    保证不同规则、不同租户、不同重置周期互不阻塞；
/// 4) SequenceExhausted 由 Allocate* 语句的 OUTPUT 为空表示，禁止在应用层做 MaxValue 预检——会引入 TOCTOU。
/// </summary>
internal sealed class SerialNumberAllocator(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions) : ISerialNumberAllocator
{
    /// <summary>
    /// 分配一个流水号。同一 (ruleKey, idempotencyKey) 重复请求返回首次分配的 SerialNumber，
    /// 不重复扣减计数器；Scope=Host 可在无租户上下文调用，Scope=Tenant 必须有受信任租户上下文，
    /// 否则返回 TenantContextRequired。
    /// </summary>
    public async Task<Result<SerialNumberAllocation>> AllocateAsync(
        string ruleKey,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedRuleKey = ruleKey?.Trim() ?? string.Empty;
        var normalizedIdempotencyKey = idempotencyKey?.Trim() ?? string.Empty;
        if (!IsSafeKey(normalizedRuleKey, 128))
        {
            return InvalidRule();
        }

        if (!IsSafeKey(normalizedIdempotencyKey, 128))
        {
            return Failure(
                SerialNumberErrorCodes.IdempotencyKeyInvalid,
                "The serial number idempotency key is invalid.",
                ErrorType.Validation);
        }

        if (!currentTenant.IsAvailable)
        {
            return TenantRequired();
        }

        try
        {
            return await transaction.ExecuteAsync(
                    token => AllocateCoreAsync(
                        normalizedRuleKey,
                        normalizedIdempotencyKey,
                        token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DataCommandException exception)
            when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            var replay = await FindReplayAfterConflictAsync(
                    normalizedRuleKey,
                    normalizedIdempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (replay is not null)
            {
                return Result<SerialNumberAllocation>.Success(Map(replay));
            }

            throw;
        }
    }

    private async Task<Result<SerialNumberAllocation>> AllocateCoreAsync(
        string ruleKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var rule = await FindRuleForAllocationAsync(ruleKey, cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            return Failure(
                SerialNumberErrorCodes.RuleNotFound,
                "The serial number rule was not found.",
                ErrorType.NotFound);
        }

        if (!rule.IsEnabled)
        {
            return Failure(
                SerialNumberErrorCodes.RuleDisabled,
                "The serial number rule is disabled.",
                ErrorType.Conflict);
        }

        var scope = (SerialNumberRuleScope)rule.Scope;
        var tenant = ResolveTenant(scope);
        if (!tenant.IsSuccess)
        {
            return TenantRequired();
        }

        var tenantId = tenant.Value!.TenantId;
        var tenantIdentifier = tenant.Value.TenantIdentifier;
        var pattern = SerialNumberPattern.Parse(rule.Pattern, scope);
        if (!pattern.IsSuccess)
        {
            return Failure(
                SerialNumberErrorCodes.PatternInvalid,
                "The persisted serial number pattern is invalid.",
                ErrorType.Conflict);
        }

        var existing = await FindExistingAsync(
                rule.Id,
                tenantId,
                idempotencyKey,
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<SerialNumberAllocation>.Success(Map(existing));
        }

        var now = clock.UtcNow;
        var resetBucket = SerialNumberResetBucket.Create(
            (SerialNumberResetInterval)rule.ResetInterval,
            now);
        var counterStatement = SelectCounterStatement(tenantId);
        var counter = await queryExecutor
            .QuerySingleOrDefaultAsync<AllocatedCounterValue>(
                counterStatement,
                SerialNumbersSqlParameters.Create(
                    ("CounterId", idGenerator.NewId()),
                    ("RuleId", rule.Id),
                    ("TenantId", tenantId),
                    ("ResetBucket", resetBucket),
                    ("LockResource", CreateLockResource(
                        rule.Id,
                        tenantId,
                        resetBucket)),
                    ("MinimumValue", rule.MinimumValue),
                    ("MaximumValue", rule.MaximumValue),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (counter is null || counter.Value < rule.MinimumValue)
        {
            return Failure(
                SerialNumberErrorCodes.SequenceExhausted,
                "The serial number sequence is exhausted.",
                ErrorType.Conflict);
        }

        var serialNumber = pattern.Value!.Format(
            now,
            tenantIdentifier,
            counter.Value);
        var allocation = new SerialNumberAllocation(
            rule.RuleKey,
            serialNumber,
            counter.Value,
            resetBucket,
            now);
        await commandExecutor.ExecuteAsync(
                tenantId is null
                    ? SerialNumberSql.InsertHostAllocation
                    : SerialNumberSql.InsertTenantAllocation,
                SerialNumbersSqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("RuleId", rule.Id),
                    ("TenantId", tenantId),
                    ("RuleKey", rule.RuleKey),
                    ("ResetBucket", resetBucket),
                    ("IdempotencyKey", idempotencyKey),
                    ("SequenceValue", counter.Value),
                    ("SerialNumber", serialNumber),
                    ("AllocatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<SerialNumberAllocation>.Success(allocation);
    }

    private Task<SerialNumberRuleRecord?> FindRuleForAllocationAsync(
        string ruleKey,
        CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                SerialNumberSql.LockRuleForAllocationSqlServer,
            DatabaseProvider.MySql =>
                SerialNumberSql.LockRuleForAllocationMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        return queryExecutor.QuerySingleOrDefaultAsync<SerialNumberRuleRecord>(
            statement,
            SerialNumbersSqlParameters.Create(("RuleKey", ruleKey)),
            cancellationToken);
    }

    private async Task<SerialNumberAllocationRecord?>
        FindReplayAfterConflictAsync(
            string ruleKey,
            string idempotencyKey,
            CancellationToken cancellationToken)
    {
        var rule = await queryExecutor
            .QuerySingleOrDefaultAsync<SerialNumberRuleRecord>(
                SerialNumberSql.FindRuleByKey,
                SerialNumbersSqlParameters.Create(("RuleKey", ruleKey)),
                cancellationToken)
            .ConfigureAwait(false);
        if (rule is null)
        {
            return null;
        }

        var tenant = ResolveTenant((SerialNumberRuleScope)rule.Scope);
        return tenant.IsSuccess
            ? await FindExistingAsync(
                    rule.Id,
                    tenant.Value!.TenantId,
                    idempotencyKey,
                    cancellationToken)
                .ConfigureAwait(false)
            : null;
    }

    private Result<AllocationTenant> ResolveTenant(
        SerialNumberRuleScope scope)
    {
        if (scope == SerialNumberRuleScope.Host)
        {
            return Result<AllocationTenant>.Success(new(null, null));
        }

        return !currentTenant.IsHost
               && currentTenant.Id is not null
               && !string.IsNullOrWhiteSpace(currentTenant.Identifier)
            ? Result<AllocationTenant>.Success(new(
                currentTenant.Id,
                currentTenant.Identifier))
            : Result<AllocationTenant>.Failure(new Error(
                SerialNumberErrorCodes.TenantContextRequired,
                "The serial number rule requires a trusted tenant context.",
                ErrorType.Forbidden));
    }

    private SqlStatement SelectCounterStatement(Guid? tenantId) =>
        (databaseOptions.Value.Provider, tenantId is null) switch
        {
            (DatabaseProvider.SqlServer, true) =>
                SerialNumberSql.AllocateHostSqlServer,
            (DatabaseProvider.SqlServer, false) =>
                SerialNumberSql.AllocateTenantSqlServer,
            (DatabaseProvider.MySql, true) =>
                SerialNumberSql.AllocateHostMySql,
            (DatabaseProvider.MySql, false) =>
                SerialNumberSql.AllocateTenantMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };

    private Task<SerialNumberAllocationRecord?> FindExistingAsync(
        Guid ruleId,
        Guid? tenantId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<SerialNumberAllocationRecord>(
            tenantId is null
                ? SerialNumberSql.FindHostAllocation
                : SerialNumberSql.FindTenantAllocation,
            SerialNumbersSqlParameters.Create(
                ("RuleId", ruleId),
                ("TenantId", tenantId),
                ("IdempotencyKey", idempotencyKey)),
            cancellationToken);

    private static bool IsSafeKey(string value, int maximumLength) =>
        value.Length is >= 1
        && value.Length <= maximumLength
        && value.All(character =>
            character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '.'
            or '_'
            or '-'
            or ':');

    private static string CreateLockResource(
        Guid ruleId,
        Guid? tenantId,
        string resetBucket) =>
        $"Full.NET.SerialNumbers:{ruleId:N}:{tenantId?.ToString("N") ?? "host"}:{resetBucket}";

    private static SerialNumberAllocation Map(
        SerialNumberAllocationRecord record) =>
        new(
            record.RuleKey,
            record.SerialNumber,
            record.SequenceValue,
            record.ResetBucket,
            record.AllocatedAtUtc);

    private static Result<SerialNumberAllocation> InvalidRule() =>
        Failure(
            SerialNumberErrorCodes.RuleInvalid,
            "The serial number rule key is invalid.",
            ErrorType.Validation);

    private static Result<SerialNumberAllocation> TenantRequired() =>
        Failure(
            SerialNumberErrorCodes.TenantContextRequired,
            "The serial number rule requires a trusted tenant context.",
            ErrorType.Forbidden);

    private static Result<SerialNumberAllocation> Failure(
        string code,
        string message,
        ErrorType type) =>
        Result<SerialNumberAllocation>.Failure(new Error(code, message, type));

    private sealed record AllocationTenant(
        Guid? TenantId,
        string? TenantIdentifier);
}
