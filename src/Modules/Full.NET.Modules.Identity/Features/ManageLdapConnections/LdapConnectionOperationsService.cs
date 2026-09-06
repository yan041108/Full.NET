using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Directory;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;

namespace Full.NET.Modules.Identity.Features.ManageLdapConnections;

/// <summary>LDAP 连接测试与同步预览操作。</summary>
internal sealed class LdapConnectionOperationsService(
    IQueryExecutor queryExecutor,
    LdapBindPasswordProtector bindPasswordProtector,
    ILdapDirectoryClient ldapDirectoryClient)
{
    /// <summary>预览条目默认上限。</summary>
    internal const int DefaultPreviewMaxEntries = 50;

    /// <summary>预览条目绝对上限。</summary>
    internal const int AbsolutePreviewMaxEntries = 200;

    /// <summary>测试 LDAP 连接与服务账号绑定。</summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>测试结果。</returns>
    public async Task<Result<TestLdapConnectionResult>> TestConnectionAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var settingsResult = await LoadRuntimeSettingsAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (!settingsResult.IsSuccess)
        {
            return Result<TestLdapConnectionResult>.Failure(settingsResult.Error!);
        }

        var outcome = await ldapDirectoryClient.TestConnectionAsync(
                settingsResult.Value!,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<TestLdapConnectionResult>.Success(
            new TestLdapConnectionResult(outcome.Succeeded, outcome.Message));
    }

    /// <summary>以搜索到的用户 DN 测试 LDAP 用户认证。</summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="request">认证测试请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认证测试结果。</returns>
    public async Task<Result<TestLdapAuthenticationResult>> TestAuthenticationAsync(
        Guid connectionId,
        TestLdapAuthenticationRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account?.Trim() ?? string.Empty;
        if (account.Length == 0 || string.IsNullOrEmpty(request.Password))
        {
            return Result<TestLdapAuthenticationResult>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionInvalidMetadata,
                "Account and password are required for authentication test.",
                ErrorType.Validation));
        }

        var settingsResult = await LoadRuntimeSettingsAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (!settingsResult.IsSuccess)
        {
            return Result<TestLdapAuthenticationResult>.Failure(settingsResult.Error!);
        }

        var outcome = await ldapDirectoryClient.TestAuthenticationAsync(
                settingsResult.Value!,
                account,
                request.Password,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<TestLdapAuthenticationResult>.Success(
            new TestLdapAuthenticationResult(
                outcome.Succeeded,
                outcome.MatchedDn,
                outcome.Message));
    }

    /// <summary>预览 LDAP 目录条目；不写入本地用户或机构表。</summary>
    /// <param name="connectionId">连接标识。</param>
    /// <param name="request">预览请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>预览响应。</returns>
    public async Task<Result<PreviewLdapSyncResponse>> PreviewSyncAsync(
        Guid connectionId,
        PreviewLdapSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        var settingsResult = await LoadRuntimeSettingsAsync(connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (!settingsResult.IsSuccess)
        {
            return Result<PreviewLdapSyncResponse>.Failure(settingsResult.Error!);
        }

        var settings = settingsResult.Value!;
        var searchBaseDn = string.IsNullOrWhiteSpace(request.SearchBaseDn)
            ? settings.SyncSearchBaseDn
            : request.SearchBaseDn.Trim();
        if (!LdapDnScopeValidator.IsSameOrSubordinate(searchBaseDn, settings.SyncSearchBaseDn))
        {
            return Result<PreviewLdapSyncResponse>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionPreviewSearchBaseOutOfScope,
                "Preview search base DN must stay within the configured sync search base.",
                ErrorType.Validation));
        }

        var maxEntries = Math.Clamp(
            request.MaxEntries ?? DefaultPreviewMaxEntries,
            1,
            AbsolutePreviewMaxEntries);
        var entries = await ldapDirectoryClient.PreviewEntriesAsync(
                settings,
                searchBaseDn,
                maxEntries,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PreviewLdapSyncResponse>.Success(
            new PreviewLdapSyncResponse(
                searchBaseDn,
                entries.Select(entry => new LdapSyncPreviewEntry(
                    entry.Dn,
                    entry.EntryKind,
                    entry.Account,
                    entry.DisplayName,
                    entry.Mail,
                    entry.DepartmentCode)).ToArray()));
    }

    private async Task<Result<LdapConnectionRuntimeSettings>> LoadRuntimeSettingsAsync(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<LdapConnectionRecord>(
                LdapConnectionSql.FindById,
                IdentitySqlParameters.Create(("ConnectionId", connectionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Result<LdapConnectionRuntimeSettings>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionNotFound,
                "The LDAP connection was not found.",
                ErrorType.NotFound));
        }

        string bindPassword;
        try
        {
            bindPassword = bindPasswordProtector.Unprotect(row.BindPasswordProtected);
        }
        catch (Exception exception)
        {
            return Result<LdapConnectionRuntimeSettings>.Failure(new Error(
                IdentityErrorCodes.LdapConnectionInvalidMetadata,
                $"Unable to decrypt bind password: {exception.Message}",
                ErrorType.Validation));
        }

        return Result<LdapConnectionRuntimeSettings>.Success(
            new LdapConnectionRuntimeSettings(
                row.Host,
                row.Port,
                row.UseTls,
                row.BaseDn,
                row.BindDn,
                bindPassword,
                row.UserSearchFilter,
                row.UserAccountAttribute,
                row.EmployeeIdAttribute,
                row.DepartmentCodeAttribute,
                row.SyncSearchBaseDn));
    }
}
