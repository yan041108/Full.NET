using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Directory;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.ManageLdapConnections;

/// <summary>LDAP 连接创建、更新、禁用与删除。</summary>
internal sealed class LdapConnectionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    LdapConnectionQueryService queries,
    IIdentityActiveTenantDirectory activeTenants,
    LdapBindPasswordProtector bindPasswordProtector,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>名称允许的最大字符数。</summary>
    internal const int MaxNameLength = 128;

    /// <summary>主机名允许的最大字符数。</summary>
    internal const int MaxHostLength = 256;

    /// <summary>DN 与过滤器允许的最大字符数。</summary>
    internal const int MaxDnLength = 512;

    /// <summary>属性名允许的最大字符数。</summary>
    internal const int MaxAttributeLength = 128;

    /// <summary>用户搜索过滤器允许的最大字符数。</summary>
    internal const int MaxFilterLength = 256;

    /// <summary>默认用户搜索过滤器。</summary>
    internal const string DefaultUserSearchFilter = "(sAMAccountName={0})";

    /// <summary>默认用户账号属性。</summary>
    internal const string DefaultUserAccountAttribute = "sAMAccountName";

    /// <summary>创建 LDAP 连接。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<LdapConnectionResponse>> CreateAsync(
        CreateLdapConnectionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    /// <summary>更新 LDAP 连接。</summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的连接或稳定业务错误。</returns>
    public Task<Result<LdapConnectionResponse>> UpdateAsync(
        Guid connectionId,
        UpdateLdapConnectionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(connectionId, request, token),
            cancellationToken);

    /// <summary>禁用 LDAP 连接。</summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>禁用后的连接或稳定业务错误。</returns>
    public Task<Result<LdapConnectionResponse>> DisableAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(connectionId, token),
            cancellationToken);

    /// <summary>删除 LDAP 连接。</summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除成功或稳定业务错误。</returns>
    public Task<Result<bool>> DeleteAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(connectionId, token),
            cancellationToken);

    private async Task<Result<LdapConnectionResponse>> CreateCoreAsync(
        CreateLdapConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var metadataValidation = ValidateMetadata(
            request.Name,
            request.Host,
            request.Port,
            request.BaseDn,
            request.BindDn,
            request.UserSearchFilter,
            request.UserAccountAttribute,
            request.EmployeeIdAttribute,
            request.DepartmentCodeAttribute,
            request.SyncSearchBaseDn);
        if (!metadataValidation.IsSuccess)
        {
            return Result<LdapConnectionResponse>.Failure(metadataValidation.Error!);
        }

        if (string.IsNullOrWhiteSpace(request.BindPassword))
        {
            return Result<LdapConnectionResponse>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionBindPasswordRequired,
                "Bind password is required when creating an LDAP connection.",
                ErrorType.Validation));
        }

        var tenantValidation = await ValidateTenantScopeAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantValidation.IsSuccess)
        {
            return Result<LdapConnectionResponse>.Failure(tenantValidation.Error!);
        }

        if (!LdapDnScopeValidator.IsSameOrSubordinate(
                metadataValidation.Value!.SyncSearchBaseDn,
                metadataValidation.Value.BaseDn))
        {
            return ScopeValidationFailure<LdapConnectionResponse>();
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<LdapConnectionRecord>(
                LdapConnectionSql.FindByTenantScope,
                IdentitySqlParameters.Create(("TenantId", request.TenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return ScopeAlreadyConfigured();
        }

        var now = clock.UtcNow;
        var connectionId = idGenerator.NewId();
        var record = new LdapConnectionRecord
        {
            Id = connectionId,
            TenantId = request.TenantId,
            Name = metadataValidation.Value.Name,
            Host = metadataValidation.Value.Host,
            Port = metadataValidation.Value.Port,
            UseTls = request.UseTls,
            BaseDn = metadataValidation.Value.BaseDn,
            BindDn = metadataValidation.Value.BindDn,
            BindPasswordProtected = bindPasswordProtector.Protect(request.BindPassword.Trim()),
            UserSearchFilter = metadataValidation.Value.UserSearchFilter,
            UserAccountAttribute = metadataValidation.Value.UserAccountAttribute,
            EmployeeIdAttribute = metadataValidation.Value.EmployeeIdAttribute,
            DepartmentCodeAttribute = metadataValidation.Value.DepartmentCodeAttribute,
            SyncSearchBaseDn = metadataValidation.Value.SyncSearchBaseDn,
            IsEnabled = request.IsEnabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = null,
            Version = 1,
        };
        await commandExecutor.ExecuteAsync(
                LdapConnectionSql.Insert,
                record,
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<LdapConnectionResponse>> UpdateCoreAsync(
        Guid connectionId,
        UpdateLdapConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<LdapConnectionRecord>(
                LdapConnectionSql.FindById,
                IdentitySqlParameters.Create(("ConnectionId", connectionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFound();
        }

        var metadataValidation = ValidateMetadata(
            request.Name,
            request.Host,
            request.Port,
            request.BaseDn,
            request.BindDn,
            request.UserSearchFilter,
            request.UserAccountAttribute,
            request.EmployeeIdAttribute,
            request.DepartmentCodeAttribute,
            request.SyncSearchBaseDn);
        if (!metadataValidation.IsSuccess)
        {
            return Result<LdapConnectionResponse>.Failure(metadataValidation.Error!);
        }

        if (!LdapDnScopeValidator.IsSameOrSubordinate(
                metadataValidation.Value!.SyncSearchBaseDn,
                metadataValidation.Value.BaseDn))
        {
            return ScopeValidationFailure<LdapConnectionResponse>();
        }

        var protectedPassword = string.IsNullOrWhiteSpace(request.BindPassword)
            ? current.BindPasswordProtected
            : bindPasswordProtector.Protect(request.BindPassword.Trim());
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                LdapConnectionSql.Update,
                IdentitySqlParameters.Create(
                    ("ConnectionId", connectionId),
                    ("Name", metadataValidation.Value.Name),
                    ("Host", metadataValidation.Value.Host),
                    ("Port", metadataValidation.Value.Port),
                    ("UseTls", request.UseTls),
                    ("BaseDn", metadataValidation.Value.BaseDn),
                    ("BindDn", metadataValidation.Value.BindDn),
                    ("BindPasswordProtected", protectedPassword),
                    ("UserSearchFilter", metadataValidation.Value.UserSearchFilter),
                    ("UserAccountAttribute", metadataValidation.Value.UserAccountAttribute),
                    ("EmployeeIdAttribute", metadataValidation.Value.EmployeeIdAttribute),
                    ("DepartmentCodeAttribute", metadataValidation.Value.DepartmentCodeAttribute),
                    ("SyncSearchBaseDn", metadataValidation.Value.SyncSearchBaseDn),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<LdapConnectionResponse>> DisableCoreAsync(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<LdapConnectionRecord>(
                LdapConnectionSql.FindById,
                IdentitySqlParameters.Create(("ConnectionId", connectionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                LdapConnectionSql.Disable,
                IdentitySqlParameters.Create(
                    ("ConnectionId", connectionId),
                    ("UpdatedAtUtc", now),
                    ("Version", current.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return VersionConflict();
        }

        return await queries.GetByIdAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<LdapConnectionRecord>(
                LdapConnectionSql.FindById,
                IdentitySqlParameters.Create(("ConnectionId", connectionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionNotFound,
                "The LDAP connection was not found.",
                ErrorType.NotFound));
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                LdapConnectionSql.Delete,
                IdentitySqlParameters.Create(("ConnectionId", connectionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows < 1)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionNotFound,
                "The LDAP connection was not found.",
                ErrorType.NotFound));
        }

        return Result<bool>.Success(true);
    }

    private async Task<Result<bool>> ValidateTenantScopeAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (!tenantId.HasValue)
        {
            return Result<bool>.Success(true);
        }

        if (!await activeTenants.IsActiveTenantAsync(tenantId.Value, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionTenantInactive,
                "The target tenant was not found or is inactive.",
                ErrorType.Validation));
        }

        return Result<bool>.Success(true);
    }

    internal static Result<ValidatedMetadata> ValidateMetadata(
        string? name,
        string? host,
        int port,
        string? baseDn,
        string? bindDn,
        string? userSearchFilter,
        string? userAccountAttribute,
        string? employeeIdAttribute,
        string? departmentCodeAttribute,
        string? syncSearchBaseDn)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > MaxNameLength)
        {
            return InvalidMetadata("LDAP connection name is invalid.");
        }

        var normalizedHost = host?.Trim() ?? string.Empty;
        if (normalizedHost.Length is < 1 or > MaxHostLength)
        {
            return InvalidMetadata("LDAP host is invalid.");
        }

        if (port is < 1 or > 65535)
        {
            return InvalidMetadata("LDAP port is invalid.");
        }

        var normalizedBaseDn = NormalizeRequiredDn(baseDn);
        if (!normalizedBaseDn.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedBaseDn.Error!);
        }

        var normalizedBindDn = NormalizeRequiredDn(bindDn);
        if (!normalizedBindDn.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedBindDn.Error!);
        }

        var normalizedSyncSearchBaseDn = NormalizeRequiredDn(syncSearchBaseDn);
        if (!normalizedSyncSearchBaseDn.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedSyncSearchBaseDn.Error!);
        }

        var normalizedFilter = userSearchFilter?.Trim();
        if (string.IsNullOrEmpty(normalizedFilter))
        {
            normalizedFilter = DefaultUserSearchFilter;
        }
        else if (normalizedFilter.Length > MaxFilterLength || !normalizedFilter.Contains("{0}", StringComparison.Ordinal))
        {
            return InvalidMetadata("User search filter must contain the {0} placeholder.");
        }

        var normalizedAccountAttribute = userAccountAttribute?.Trim();
        if (string.IsNullOrEmpty(normalizedAccountAttribute))
        {
            normalizedAccountAttribute = DefaultUserAccountAttribute;
        }
        else if (normalizedAccountAttribute.Length > MaxAttributeLength)
        {
            return InvalidMetadata("User account attribute is invalid.");
        }

        var normalizedEmployeeIdAttribute = NormalizeOptionalAttribute(employeeIdAttribute);
        if (!normalizedEmployeeIdAttribute.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedEmployeeIdAttribute.Error!);
        }

        var normalizedDepartmentCodeAttribute = NormalizeOptionalAttribute(departmentCodeAttribute);
        if (!normalizedDepartmentCodeAttribute.IsSuccess)
        {
            return Result<ValidatedMetadata>.Failure(normalizedDepartmentCodeAttribute.Error!);
        }

        return Result<ValidatedMetadata>.Success(new ValidatedMetadata(
            normalizedName,
            normalizedHost,
            port,
            normalizedBaseDn.Value!,
            normalizedBindDn.Value!,
            normalizedFilter,
            normalizedAccountAttribute,
            normalizedEmployeeIdAttribute.Value,
            normalizedDepartmentCodeAttribute.Value,
            normalizedSyncSearchBaseDn.Value!));
    }

    internal static Result<string> NormalizeRequiredDn(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > MaxDnLength)
        {
            return Result<string>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionInvalidMetadata,
                "Directory distinguished name is invalid.",
                ErrorType.Validation));
        }

        return Result<string>.Success(normalized);
    }

    private static Result<string?> NormalizeOptionalAttribute(string? value)
    {
        if (value is null)
        {
            return Result<string?>.Success(null);
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return Result<string?>.Success(null);
        }

        if (normalized.Length > MaxAttributeLength)
        {
            return Result<string?>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionInvalidMetadata,
                "Directory attribute name is invalid.",
                ErrorType.Validation));
        }

        return Result<string?>.Success(normalized);
    }

    private static Result<ValidatedMetadata> InvalidMetadata(string message) =>
        Result<ValidatedMetadata>.Failure(new Error(
            IdentityErrorCodes.LdapConnectionInvalidMetadata,
            message,
            ErrorType.Validation));

    private static Result<T> InvalidMetadataFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.LdapConnectionInvalidMetadata,
            message,
            ErrorType.Validation));

    private static Result<LdapConnectionResponse> NotFound() =>
        Result<LdapConnectionResponse>.Failure(new Error(
            IdentityErrorCodes.LdapConnectionNotFound,
            "The LDAP connection was not found.",
            ErrorType.NotFound));

    private static Result<LdapConnectionResponse> VersionConflict() =>
        Result<LdapConnectionResponse>.Failure(new Error(
            IdentityErrorCodes.LdapConnectionVersionConflict,
            "The LDAP connection was modified by another request.",
            ErrorType.Conflict));

    private static Result<LdapConnectionResponse> ScopeAlreadyConfigured() =>
        Result<LdapConnectionResponse>.Failure(new Error(
            IdentityErrorCodes.LdapConnectionScopeAlreadyConfigured,
            "An LDAP connection already exists for this scope.",
            ErrorType.Conflict));

    private static Result<T> ScopeValidationFailure<T>() =>
        Result<T>.Failure(new Error(
            IdentityErrorCodes.LdapConnectionInvalidSyncSearchBase,
            "Sync search base DN must be within the configured base DN.",
            ErrorType.Validation));

    internal sealed record ValidatedMetadata(
        string Name,
        string Host,
        int Port,
        string BaseDn,
        string BindDn,
        string UserSearchFilter,
        string UserAccountAttribute,
        string? EmployeeIdAttribute,
        string? DepartmentCodeAttribute,
        string SyncSearchBaseDn);
}
