using System.Globalization;
using System.Text.Json;
using Full.NET.Abstractions.Auditing;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.Modules.Settings.Features.ManageDiagnosticPolicy;

/// <summary>Host 限时诊断策略读写；更新与 B0 审计同事务，提交后直接刷新缓存。</summary>
internal sealed class DiagnosticPolicyManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ITransactionalDomainAuditWriter<DiagnosticPolicyAuditWrite> domainAuditWriter,
    DiagnosticPolicyCacheInvalidator cacheInvalidator,
    IDiagnosticPolicyStore policyStore,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<DiagnosticPolicyResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await policyStore.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var row = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryRow>(
                ConfigEntrySql.FindByKey,
                new { ConfigKey = DiagnosticPolicyLimits.ConfigKey },
                cancellationToken)
            .ConfigureAwait(false);
        return Result<DiagnosticPolicyResponse>.Success(MapResponse(snapshot, row?.Version ?? 0));
    }

    public async Task<Result<DiagnosticPolicyResponse>> UpdateAsync(
        UpdateDiagnosticPolicyRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request, clock.UtcNow);
        if (validation is not null)
        {
            return Result<DiagnosticPolicyResponse>.Failure(validation);
        }

        Result<DiagnosticPolicyResponse>? committed = null;
        await transaction.ExecuteAsync(
                async token =>
                {
                    committed = await UpdateCoreAsync(request, token).ConfigureAwait(false);
                    return committed;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (committed is { IsSuccess: true })
        {
            await cacheInvalidator.InvalidateAfterCommitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await policyStore.RefreshAsync(committed.Value!.Version, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return committed ?? Result<DiagnosticPolicyResponse>.Failure(
            new Error("settings.diagnostic_policy.update_failed", "Update failed.", ErrorType.Unexpected));
    }

    public async Task<Result<DiagnosticPolicyResponse>> RestoreAsync(
        int configEntryVersion,
        CancellationToken cancellationToken)
    {
        Result<DiagnosticPolicyResponse>? committed = null;
        await transaction.ExecuteAsync(
                async token =>
                {
                    committed = await RestoreCoreAsync(configEntryVersion, token).ConfigureAwait(false);
                    return committed;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (committed is { IsSuccess: true })
        {
            await cacheInvalidator.InvalidateAfterCommitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await policyStore.RefreshAsync(0, CancellationToken.None).ConfigureAwait(false);
        }

        return committed ?? Result<DiagnosticPolicyResponse>.Failure(
            new Error("settings.diagnostic_policy.restore_failed", "Restore failed.", ErrorType.Unexpected));
    }

    private async Task<Result<DiagnosticPolicyResponse>> UpdateCoreAsync(
        UpdateDiagnosticPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var pressure = ParsePressure(request.PressureState);
        var rules = request.Rules.Select(MapRule).ToArray();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryRow>(
                ConfigEntrySql.FindByKey,
                new { ConfigKey = DiagnosticPolicyLimits.ConfigKey },
                cancellationToken)
            .ConfigureAwait(false);

        long nextVersion = 1;
        Guid entityId;
        if (existing is null)
        {
            entityId = idGenerator.NewId();
            var document = new DiagnosticPolicyDocument(nextVersion, pressure, rules);
            await commandExecutor.ExecuteAsync(
                    ConfigEntrySql.Insert,
                    new
                    {
                        Id = entityId,
                        ConfigKey = DiagnosticPolicyLimits.ConfigKey,
                        DisplayName = "Logging diagnostic policy",
                        Description = "Expiring Host diagnostic sampling and Best Effort capacity overrides.",
                        // 诊断策略占用独立 ConfigKey，不进入配置分组目录。
                        GroupName = (string?)null,
                        ValueKind = ConfigValueKinds.Json,
                        Value = JsonSerializer.Serialize(document, JsonOptions),
                        DisplayOrder = 0,
                        IsActive = true,
                        CreatedAtUtc = now,
                        Version = 1,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (existing.Version != request.ConfigEntryVersion)
            {
                return Result<DiagnosticPolicyResponse>.Failure(
                    new Error("settings.diagnostic_policy.version_conflict", "Configuration entry version conflict.", ErrorType.Conflict));
            }

            entityId = existing.Id;
            nextVersion = ReadDocumentVersion(existing.Value) + 1;
            var document = new DiagnosticPolicyDocument(nextVersion, pressure, rules);
            var affected = await commandExecutor.ExecuteAsync(
                    ConfigEntrySql.UpdateHostConfigEntry,
                    new
                    {
                        ConfigEntryId = existing.Id,
                        DisplayName = existing.DisplayName,
                        Description = existing.Description,
                        GroupName = existing.GroupName,
                        Value = JsonSerializer.Serialize(document, JsonOptions),
                        DisplayOrder = existing.DisplayOrder,
                        UpdatedAtUtc = now,
                        Version = existing.Version,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1)
            {
                return Result<DiagnosticPolicyResponse>.Failure(
                    new Error("settings.diagnostic_policy.version_conflict", "Configuration entry version conflict.", ErrorType.Conflict));
            }
        }

        await domainAuditWriter.WriteAsync(
                new DiagnosticPolicyAuditWrite(
                    DiagnosticPolicyAuditActionKeys.Updated,
                    entityId,
                    TenantId: null,
                    DiagnosticPolicyAuditOutcomes.Success,
                    ActorUserId: null,
                    ActorDisplayName: null,
                    DiffSummaryJson: JsonSerializer.Serialize(
                        new { version = nextVersion, pressure = pressure.ToString() },
                        JsonOptions)),
                cancellationToken)
            .ConfigureAwait(false);

        var snapshot = DiagnosticPolicyStore.Materialize(
            new DiagnosticPolicyDocument(nextVersion, pressure, rules),
            now);
        return Result<DiagnosticPolicyResponse>.Success(
            MapResponse(snapshot, existing is null ? 1 : existing.Version + 1));
    }

    private async Task<Result<DiagnosticPolicyResponse>> RestoreCoreAsync(
        int configEntryVersion,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<ConfigEntryRow>(
                ConfigEntrySql.FindByKey,
                new { ConfigKey = DiagnosticPolicyLimits.ConfigKey },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            var snapshot = DiagnosticPolicySnapshot.CreateDefault(clock.UtcNow);
            return Result<DiagnosticPolicyResponse>.Success(MapResponse(snapshot, 0));
        }

        if (existing.Version != configEntryVersion)
        {
            return Result<DiagnosticPolicyResponse>.Failure(
                new Error("settings.diagnostic_policy.version_conflict", "Configuration entry version conflict.", ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var empty = new DiagnosticPolicyDocument(0, LoggingPressureState.Normal, []);
        var affected = await commandExecutor.ExecuteAsync(
                ConfigEntrySql.UpdateHostConfigEntry,
                new
                {
                    ConfigEntryId = existing.Id,
                    DisplayName = existing.DisplayName,
                    Description = existing.Description,
                    GroupName = existing.GroupName,
                    Value = JsonSerializer.Serialize(empty, JsonOptions),
                    DisplayOrder = existing.DisplayOrder,
                    UpdatedAtUtc = now,
                    Version = existing.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<DiagnosticPolicyResponse>.Failure(
                new Error("settings.diagnostic_policy.version_conflict", "Configuration entry version conflict.", ErrorType.Conflict));
        }

        await domainAuditWriter.WriteAsync(
                new DiagnosticPolicyAuditWrite(
                    DiagnosticPolicyAuditActionKeys.Updated,
                    existing.Id,
                    TenantId: null,
                    DiagnosticPolicyAuditOutcomes.Success,
                    ActorUserId: null,
                    ActorDisplayName: null,
                    DiffSummaryJson: """{"restored":true}"""),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<DiagnosticPolicyResponse>.Success(
            MapResponse(DiagnosticPolicySnapshot.CreateDefault(now), existing.Version + 1));
    }

    private static Error? Validate(UpdateDiagnosticPolicyRequest request, DateTimeOffset utcNow)
    {
        if (request.Rules is null)
        {
            return new Error("settings.diagnostic_policy.rules_required", "Rules are required.", ErrorType.Validation);
        }

        if (request.Rules.Count > DiagnosticPolicyLimits.MaxActiveRules)
        {
            return new Error(
                "settings.diagnostic_policy.too_many_rules",
                $"At most {DiagnosticPolicyLimits.MaxActiveRules} active rules are allowed.",
                ErrorType.Validation);
        }

        var tenantCount = 0;
        var traceCount = 0;
        foreach (var rule in request.Rules)
        {
            if (!TryParseScope(rule.ScopeKind, out var scopeKind))
            {
                return new Error("settings.diagnostic_policy.invalid_scope", "ScopeKind must be Category, DiagnosticGroup, Endpoint, Trace, or Tenant.", ErrorType.Validation);
            }

            if (string.IsNullOrWhiteSpace(rule.ScopeValue) || rule.ScopeValue.Length > 128)
            {
                return new Error("settings.diagnostic_policy.invalid_scope_value", "ScopeValue is required and must be <= 128 characters.", ErrorType.Validation);
            }

            if (rule.ExpiresAtUtc < utcNow + DiagnosticPolicyLimits.MinTtl
                || rule.ExpiresAtUtc > utcNow + DiagnosticPolicyLimits.MaxTtl)
            {
                return new Error("settings.diagnostic_policy.invalid_ttl", "Rule TTL must be between 1 minute and 2 hours.", ErrorType.Validation);
            }

            if (rule.SuccessSampleRateOverride is < 0 or > 1)
            {
                return new Error("settings.diagnostic_policy.invalid_sample_rate", "SuccessSampleRateOverride must be in [0,1].", ErrorType.Validation);
            }

            if ((rule.BestEffortCapacityOverride is int bec && bec <= 0)
                || (rule.MaxRequestPayloadBytesOverride is int mreq && mreq <= 0)
                || (rule.MaxResponsePayloadBytesOverride is int mres && mres <= 0))
            {
                return new Error("settings.diagnostic_policy.invalid_budget", "Capacity and byte budgets must be positive when set.", ErrorType.Validation);
            }

            if (scopeKind == DiagnosticPolicyScopeKind.Tenant)
            {
                tenantCount++;
                if (!Guid.TryParse(rule.ScopeValue, out _))
                {
                    return new Error("settings.diagnostic_policy.invalid_tenant", "Tenant scope value must be a GUID.", ErrorType.Validation);
                }
            }

            if (scopeKind == DiagnosticPolicyScopeKind.Trace)
            {
                traceCount++;
            }
        }

        if (tenantCount > DiagnosticPolicyLimits.MaxTenantScopedRules
            || traceCount > DiagnosticPolicyLimits.MaxTraceScopedRules)
        {
            return new Error("settings.diagnostic_policy.directed_limit", "Tenant/Trace directed rules exceed hard limits.", ErrorType.Validation);
        }

        if (!TryParsePressure(request.PressureState, out _))
        {
            return new Error("settings.diagnostic_policy.invalid_pressure", "PressureState must be Normal, Degraded, or Critical.", ErrorType.Validation);
        }

        return null;
    }

    private static DiagnosticPolicyRule MapRule(DiagnosticPolicyRuleRequest rule)
    {
        _ = TryParseScope(rule.ScopeKind, out var scopeKind);
        return new DiagnosticPolicyRule(
            scopeKind,
            rule.ScopeValue.Trim(),
            rule.SuccessSampleRateOverride,
            rule.BestEffortCapacityOverride,
            rule.MaxRequestPayloadBytesOverride,
            rule.MaxResponsePayloadBytesOverride,
            rule.ExpiresAtUtc);
    }

    private static LoggingPressureState ParsePressure(string value)
    {
        _ = TryParsePressure(value, out var pressure);
        return pressure;
    }

    private static bool TryParsePressure(string? value, out LoggingPressureState pressure) =>
        Enum.TryParse(value, ignoreCase: true, out pressure)
        && Enum.IsDefined(pressure);

    private static bool TryParseScope(string? value, out DiagnosticPolicyScopeKind scopeKind) =>
        Enum.TryParse(value, ignoreCase: true, out scopeKind)
        && Enum.IsDefined(scopeKind);

    private static long ReadDocumentVersion(string json)
    {
        try
        {
            var document = JsonSerializer.Deserialize<DiagnosticPolicyDocument>(json, JsonOptions);
            return document?.Version ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static DiagnosticPolicyResponse MapResponse(
        DiagnosticPolicySnapshot snapshot,
        int configEntryVersion) =>
        new(
            snapshot.Version,
            snapshot.PressureState.ToString(),
            snapshot.IsDefault,
            snapshot.LoadedAtUtc,
            snapshot.ActiveRules.Select(rule => new DiagnosticPolicyRuleResponse(
                rule.ScopeKind.ToString(),
                rule.ScopeValue,
                rule.SuccessSampleRateOverride,
                rule.BestEffortCapacityOverride,
                rule.MaxRequestPayloadBytesOverride,
                rule.MaxResponsePayloadBytesOverride,
                rule.ExpiresAtUtc)).ToArray(),
            configEntryVersion);

    private sealed record ConfigEntryRow(
        Guid Id,
        string ConfigKey,
        string DisplayName,
        string? Description,
        string? GroupName,
        string ValueKind,
        string Value,
        int DisplayOrder,
        bool IsActive,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? UpdatedAtUtc,
        int Version);
}
