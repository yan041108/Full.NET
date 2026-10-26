using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.SerialNumbers.Contracts;
using Full.NET.Modules.SerialNumbers.Features.ManageHostSerialRules;
using Full.NET.Modules.SerialNumbers.Persistence;
using Full.NET.Modules.SerialNumbers.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.SerialNumbers.Features.WorkflowCommands;

/// <summary>所有者规则CAS与命令凭据同事务；取消墓碑先于执行时禁止产生业务写入。</summary>
internal sealed class SerialRuleApprovedCommandExecutor(IQueryExecutor query, ICommandExecutor commands,
    ICommandTransaction transaction, HostSerialRuleService rules, IOptions<DatabaseOptions> database, IClock clock, ICurrentTenant tenant)
{
    public async Task<Result<DataApprovalCommandEffect>> ApplyAsync(DataApprovalScenarioCommand command, CancellationToken token)
    {
        if (!tenant.IsHost) return Denied();
        if (!SerialRuleCommandSnapshot.TryCreate(command, out var snapshot)) return Invalid();
        try
        {
            var result = await transaction.ExecuteResultAsync(inner => ApplyCoreAsync(command, snapshot!, inner), token).ConfigureAwait(false);
            // 同一调用的并发执行先竞争规则版本；失败方只能用同事务提交的真实凭据证明重放。
            if (!result.IsSuccess && result.Error!.Code == SerialNumberErrorCodes.RuleVersionConflict &&
                await FindAsync(command.CommandId, false, token).ConfigureAwait(false) is { } concurrent)
                return Effect(concurrent, command, snapshot!);
            return result;
        }
        catch (DataCommandException exception) when (exception.Kind == DataCommandFailureKind.UniqueConstraint)
        {
            var concurrent = await FindAsync(command.CommandId, false, token).ConfigureAwait(false);
            if (concurrent is null) throw;
            return Effect(concurrent, command, snapshot!);
        }
    }

    public async Task<Result<DataApprovalCommandEffect>> CompensateAsync(DataApprovalScenarioCommand command, CancellationToken token)
    {
        if (!tenant.IsHost) return Denied();
        if (!SerialRuleCommandSnapshot.TryCreate(command, out var snapshot)) return Invalid();
        // 应用与取消同时首次出现时由主键线性化；唯一键竞争后只重读一次，之后交给可靠投递重试。
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { return await transaction.ExecuteResultAsync(inner => CompensateCoreAsync(command, snapshot!, inner), token).ConfigureAwait(false); }
            catch (DataCommandException exception) when (attempt == 0 && exception.Kind == DataCommandFailureKind.UniqueConstraint) { }
        }
        return InvalidReceipt();
    }

    private async Task<Result<DataApprovalCommandEffect>> ApplyCoreAsync(DataApprovalScenarioCommand command, SerialRuleCommandSnapshot snapshot, CancellationToken token)
    {
        if (await FindAsync(command.CommandId, true, token).ConfigureAwait(false) is { } existing) return Effect(existing, command, snapshot);
        var before = await rules.GetAsync(command.TargetEntityId, token).ConfigureAwait(false);
        if (!before.IsSuccess) return Result<DataApprovalCommandEffect>.Failure(before.Error!);
        if (before.Value!.Version != snapshot.ExpectedVersion) return VersionConflict();
        var applied = snapshot.Update is { } update
            ? await rules.ApplyApprovedUpdateAsync(command.TargetEntityId, command.RequestedByUserId, update, token).ConfigureAwait(false)
            : await rules.ApplyApprovedDisableAsync(command.TargetEntityId, command.RequestedByUserId, snapshot.ExpectedVersion, token).ConfigureAwait(false);
        if (!applied.IsSuccess) return Result<DataApprovalCommandEffect>.Failure(applied.Error!);
        if (applied.Value!.Version != before.Value.Version + 1) return InvalidReceipt();
        var json = JsonSerializer.Serialize(before.Value, SerialNumbersJsonSerializerContext.Default.SerialNumberRuleResponse);
        if (await commands.ExecuteAsync(SerialRuleCommandSql.Insert, Parameters(command, snapshot, "applied", json, applied.Value.Version), token).ConfigureAwait(false) != 1)
            return InvalidReceipt();
        return Result<DataApprovalCommandEffect>.Success(new(command.CommandId, snapshot.InputHash, "applied", applied.Value.Version, null));
    }

    private async Task<Result<DataApprovalCommandEffect>> CompensateCoreAsync(DataApprovalScenarioCommand command, SerialRuleCommandSnapshot snapshot, CancellationToken token)
    {
        var existing = await FindAsync(command.CommandId, true, token).ConfigureAwait(false);
        if (existing is null)
        {
            if (await commands.ExecuteAsync(SerialRuleCommandSql.Insert, Parameters(command, snapshot, "cancelled", null, null), token).ConfigureAwait(false) != 1)
                return InvalidReceipt();
            return Result<DataApprovalCommandEffect>.Success(new(command.CommandId, snapshot.InputHash, "cancelled", null, null));
        }
        var known = Effect(existing, command, snapshot);
        if (!known.IsSuccess || existing.StatusKey != "applied") return known;
        SerialNumberRuleResponse? before;
        try { before = JsonSerializer.Deserialize(existing.BeforeSnapshotJson!, SerialNumbersJsonSerializerContext.Default.SerialNumberRuleResponse); }
        catch (JsonException) { return InvalidReceipt(); }
        if (before is null || before.Id != command.TargetEntityId || before.Version + 1 != existing.AppliedVersion) return InvalidReceipt();
        var current = await rules.GetAsync(command.TargetEntityId, token).ConfigureAwait(false);
        if (!current.IsSuccess) return Result<DataApprovalCommandEffect>.Failure(current.Error!);
        if (current.Value!.Version != existing.AppliedVersion) return CompensationConflict();
        var restore = new UpdateSerialNumberRuleRequest(before.DisplayName, before.Description, before.Scope, before.ResetInterval,
            before.Pattern, before.MinimumValue, before.MaximumValue, before.DisplayOrder, before.IsEnabled, existing.AppliedVersion!.Value);
        var restored = await rules.ApplyApprovedUpdateAsync(command.TargetEntityId, command.RequestedByUserId, restore, token).ConfigureAwait(false);
        if (!restored.IsSuccess) return Result<DataApprovalCommandEffect>.Failure(restored.Error!);
        if (await commands.ExecuteAsync(SerialRuleCommandSql.CompleteCompensation, SerialNumbersSqlParameters.Create(("Id", command.CommandId),
                ("InputHash", snapshot.InputHash), ("Revision", existing.Revision), ("CompensatedVersion", restored.Value!.Version), ("Now", clock.UtcNow)), token).ConfigureAwait(false) != 1)
            return InvalidReceipt();
        return Result<DataApprovalCommandEffect>.Success(new(command.CommandId, snapshot.InputHash, "compensated", existing.AppliedVersion, restored.Value.Version));
    }

    private Task<SerialRuleCommandRecord?> FindAsync(Guid id, bool locked, CancellationToken token) => query.QuerySingleOrDefaultAsync<SerialRuleCommandRecord>(
        locked ? database.Value.Provider == DatabaseProvider.SqlServer ? SerialRuleCommandSql.LockSqlServer : SerialRuleCommandSql.LockMySql : SerialRuleCommandSql.Find,
        SerialNumbersSqlParameters.Create(("Id", id)), token);
    private object Parameters(DataApprovalScenarioCommand command, SerialRuleCommandSnapshot snapshot, string status, string? before, long? version) =>
        SerialNumbersSqlParameters.Create(("Id", command.CommandId), ("ApprovalRequestId", command.ApprovalRequestId), ("RuleId", command.TargetEntityId),
            ("RequestedByUserId", command.RequestedByUserId), ("ScenarioKey", command.ScenarioKey), ("InputHash", snapshot.InputHash),
            ("BeforeSnapshotJson", before), ("StatusKey", status), ("AppliedVersion", version), ("Now", clock.UtcNow));
    private static Result<DataApprovalCommandEffect> Effect(SerialRuleCommandRecord row, DataApprovalScenarioCommand command, SerialRuleCommandSnapshot snapshot)
    {
        if (row.Id != command.CommandId || row.ApprovalRequestId != command.ApprovalRequestId || row.RuleId != command.TargetEntityId ||
            row.RequestedByUserId != command.RequestedByUserId || row.ScenarioKey != command.ScenarioKey || row.InputHash != snapshot.InputHash)
            return Result<DataApprovalCommandEffect>.Failure(new(SerialNumberErrorCodes.ApprovalCommandInputConflict, "The command identity has a different input.", ErrorType.Conflict));
        var valid = row.Revision > 0 && (row.StatusKey switch {
            "cancelled" => row.BeforeSnapshotJson is null && row.AppliedVersion is null && row.CompensatedVersion is null,
            "applied" => row.BeforeSnapshotJson is not null && row.AppliedVersion == snapshot.ExpectedVersion + 1 && row.CompensatedVersion is null,
            "compensated" => row.BeforeSnapshotJson is not null && row.AppliedVersion == snapshot.ExpectedVersion + 1 && row.CompensatedVersion == snapshot.ExpectedVersion + 2,
            _ => false });
        if (!valid || row.BeforeSnapshotJson is { } json && Encoding.UTF8.GetByteCount(json) > 32768) return InvalidReceipt();
        if (row.BeforeSnapshotJson is { } original)
        {
            SerialNumberRuleResponse? before;
            try { before = JsonSerializer.Deserialize(original, SerialNumbersJsonSerializerContext.Default.SerialNumberRuleResponse); }
            catch (JsonException) { return InvalidReceipt(); }
            if (before is null || before.Id != row.RuleId || before.Version != snapshot.ExpectedVersion) return InvalidReceipt();
        }
        return Result<DataApprovalCommandEffect>.Success(new(row.Id, row.InputHash, row.StatusKey, row.AppliedVersion, row.CompensatedVersion));
    }
    private static Result<DataApprovalCommandEffect> Invalid() => Result<DataApprovalCommandEffect>.Failure(new(SerialNumberErrorCodes.RuleInvalid, "The approved command is invalid.", ErrorType.Validation));
    private static Result<DataApprovalCommandEffect> Denied() => Result<DataApprovalCommandEffect>.Failure(new(SerialNumberErrorCodes.TenantContextRequired, "This command requires the host context.", ErrorType.Forbidden));
    private static Result<DataApprovalCommandEffect> VersionConflict() => Result<DataApprovalCommandEffect>.Failure(new(SerialNumberErrorCodes.RuleVersionConflict, "The rule version changed.", ErrorType.Conflict));
    private static Result<DataApprovalCommandEffect> InvalidReceipt() => Result<DataApprovalCommandEffect>.Failure(new(SerialNumberErrorCodes.ApprovalCommandReceiptInvalid, "The command receipt is invalid.", ErrorType.BusinessRule));
    private static Result<DataApprovalCommandEffect> CompensationConflict() => Result<DataApprovalCommandEffect>.Failure(new(SerialNumberErrorCodes.ApprovalCommandCompensationConflict, "A later rule update prevents compensation.", ErrorType.Conflict));
}
