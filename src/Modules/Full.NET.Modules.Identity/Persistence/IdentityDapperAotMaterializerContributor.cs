#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using Full.NET.Modules.Identity.FieldProjection;
using global::Dapper;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>
/// Identity 模块 Native AOT 行物化与 typed 命令参数绑定。
/// </summary>
/// <remarks>
/// Host 用户列表与详情、字段投影裁剪档案列序并不固定，这些投影按列名读取；
/// 其余 SQL 投影列序稳定，使用 ordinal。
/// </remarks>
internal sealed class IdentityDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<IdentityUserRecord>(ReadIdentityUserRecord);
        registrar.Register<IdentityAuthorizationRow>(ReadIdentityAuthorizationRow);
        registrar.Register<IdentityProfileRecord>(ReadIdentityProfileRecord);
        registrar.Register<RefreshSessionRecord>(ReadRefreshSessionRecord);
        registrar.Register<HostUserDirectoryRecord>(ReadHostUserDirectoryRecord);
        registrar.Register<HostTenantUserDirectoryRecord>(ReadHostTenantUserDirectoryRecord);
        registrar.Register<HostUserListRow>(ReadHostUserListRow);
        registrar.Register<HostRoleMemberRow>(ReadHostRoleMemberRow);
        registrar.Register<HostUserPreferredLocaleRow>(ReadHostUserPreferredLocaleRow);
        registrar.Register<HostUserFailedLoginCountRow>(ReadHostUserFailedLoginCountRow);
        registrar.Register<HostUserLockoutEndUtcRow>(ReadHostUserLockoutEndUtcRow);
        registrar.Register<HostUserProfileRecord>(ReadHostUserProfileRecord);
        registrar.Register<HostRoleListRow>(ReadHostRoleListRow);
        registrar.Register<IdentityRoleRecord>(ReadIdentityRoleRecord);
        registrar.Register<IdentityRolePermission>(ReadIdentityRolePermission);
        registrar.Register<IdentityUserRoleDataScopeRow>(ReadIdentityUserRoleDataScopeRow);
        registrar.Register<IdentityNavigationRecord>(ReadIdentityNavigationRecord);
        registrar.Register<HostMenuListRow>(ReadHostMenuListRow);
        registrar.Register<HostNavigationCatalogSyncService.HostMenuSyncRow>(ReadHostMenuSyncRow);
        registrar.Register<HostNavigationCatalogSyncService.HostMenuRouteNameRow>(
            ReadHostMenuRouteNameRow);
        registrar.Register<OnlineSessionListRow>(ReadOnlineSessionListRow);
        registrar.Register<OnlineSessionRevokeRow>(ReadOnlineSessionRevokeRow);
        registrar.Register<ApiKeyListRow>(ReadApiKeyListRow);
        registrar.Register<ApiKeyAuthenticationRow>(ReadApiKeyAuthenticationRow);
        registrar.Register<OpenAccessClientDetailRow>(ReadOpenAccessClientDetailRow);
        registrar.Register<OpenAccessClientAccessLogRow>(ReadOpenAccessClientAccessLogRow);
        registrar.Register<OpenAccessClientAccessKeyRow>(ReadOpenAccessClientAccessKeyRow);
        registrar.Register<OpenAccessClientQuotaRow>(ReadOpenAccessClientQuotaRow);
        registrar.Register<OpenAccessClientUsageCountRow>(ReadOpenAccessClientUsageCountRow);
        registrar.Register<RegistrationPolicyRecord>(ReadRegistrationPolicyRecord);
        registrar.Register<RegistrationWayRecord>(ReadRegistrationWayRecord);
        registrar.Register<LdapConnectionRecord>(ReadLdapConnectionRecord);
        registrar.Register<OAuthProviderRecord>(ReadOAuthProviderRecord);
        registrar.Register<OAuthPublicProviderRecord>(ReadOAuthPublicProviderRecord);
        registrar.Register<OAuthUserLinkRecord>(ReadOAuthUserLinkRecord);
        registrar.Register<OAuthUserLinkWithProviderRecord>(ReadOAuthUserLinkWithProviderRecord);
        registrar.Register<OAuthAuthorizationStateRecord>(ReadOAuthAuthorizationStateRecord);
        registrar.Register<IdentityUserTotpRecord>(ReadIdentityUserTotpRecord);
        registrar.Register<OrganizationUnitProjectionRecord>(ReadOrganizationUnitProjectionRecord);
        registrar.Register<UserFieldProjectionGrantRow>(ReadUserFieldProjectionGrantRow);
        registrar.Register<IdentityRoleFieldGrantRow>(ReadIdentityRoleFieldGrantRow);
        registrar.Register<SuperAdministratorResponse>(ReadSuperAdministratorResponse);
        registrar.Register<SuperAdministratorAuditResponse>(ReadSuperAdministratorAuditResponse);

        DapperAotParameterRegistry.Register<LoginFailureUpdate>(BindLoginFailureUpdate);
        DapperAotParameterRegistry.Register<LoginSuccessUpdate>(BindLoginSuccessUpdate);
        DapperAotParameterRegistry.Register<AuthAuditEvent>(BindAuthAuditEvent);
        DapperAotParameterRegistry.Register<RefreshSession>(BindRefreshSession);
        DapperAotParameterRegistry.Register<Features.ChangeSessionContext.RefreshSessionContextUpdate>(
            BindRefreshSessionContextUpdate);
        DapperAotParameterRegistry.Register<IdentityUserRecord>(BindIdentityUserRecord);
        DapperAotParameterRegistry.Register<InsertIdentityRole>(BindInsertIdentityRole);
        DapperAotParameterRegistry.Register<InsertIdentityNavigation>(BindInsertIdentityNavigation);
        DapperAotParameterRegistry.Register<RegistrationWayRecord>(BindRegistrationWayRecord);
        DapperAotParameterRegistry.Register<LdapConnectionRecord>(BindLdapConnectionRecord);
        DapperAotParameterRegistry.Register<OAuthProviderRecord>(BindOAuthProviderRecord);
        DapperAotParameterRegistry.Register<OAuthUserLinkRecord>(BindOAuthUserLinkRecord);
        DapperAotParameterRegistry.Register<OAuthAuthorizationStateRecord>(BindOAuthAuthorizationStateRecord);
    }

    private static IdentityUserRecord ReadIdentityUserRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadBoolean(reader, 7),
            AotDataReaderExtensions.ReadInt32(reader, 8),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            reader.GetString(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadInt32(reader, 13),
            reader.GetString(14),
            AotDataReaderExtensions.ReadInt32(reader, 15),
            reader.GetString(16),
            AotDataReaderExtensions.ReadBoolean(reader, 17),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 18));

    private static IdentityAuthorizationRow ReadIdentityAuthorizationRow(DbDataReader reader) =>
        new(
            AotDataReaderExtensions.ReadNullableString(reader, 0),
            AotDataReaderExtensions.ReadBoolean(reader, 1));

    private static IdentityProfileRecord ReadIdentityProfileRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ScopeKey = reader.GetString(1),
            Username = reader.GetString(2),
            DisplayName = reader.GetString(3),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 4),
            PreferredLocale = reader.GetString(5),
            ProfileVersion = AotDataReaderExtensions.ReadInt32(reader, 6),
            MustChangePassword = AotDataReaderExtensions.ReadBoolean(reader, 7),
            PasswordChangedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
        };

    private static RefreshSessionRecord ReadRefreshSessionRecord(DbDataReader reader) =>
        new()
        {
            SessionId = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            FamilyId = reader.GetGuid(2),
            ClientId = reader.GetString(3),
            TokenHash = reader.GetString(4),
            ExpiresAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            ConsumedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            RevokedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            ReplacedById = AotDataReaderExtensions.ReadNullableGuid(reader, 8),
            ActiveTenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 9),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
            SessionVersion = AotDataReaderExtensions.ReadInt32(reader, 11),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 12),
            ScopeKey = reader.GetString(13),
            Username = reader.GetString(14),
            NormalizedUsername = reader.GetString(15),
            DisplayName = reader.GetString(16),
            PasswordHash = reader.GetString(17),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 18),
            FailedLoginCount = AotDataReaderExtensions.ReadInt32(reader, 19),
            LockoutEndUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 20),
            SecurityStamp = reader.GetString(21),
            UserCreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 22),
            UserUpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 23),
            UserVersion = AotDataReaderExtensions.ReadInt32(reader, 24),
            PreferredLocale = reader.GetString(25),
            ProfileVersion = AotDataReaderExtensions.ReadInt32(reader, 26),
            MustChangePassword = AotDataReaderExtensions.ReadBoolean(reader, 27),
            PasswordChangedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 28),
        };

    private static HostUserDirectoryRecord ReadHostUserDirectoryRecord(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));

    private static HostTenantUserDirectoryRecord ReadHostTenantUserDirectoryRecord(
        DbDataReader reader) =>
        new(
            ReadGuidByName(reader, "Id"),
            ReadStringByName(reader, "Username"),
            ReadStringByName(reader, "DisplayName"),
            ReadStringByName(reader, "AccountType"),
            ReadBooleanByName(reader, "IsActive"),
            ReadStringByName(reader, "PreferredLocale"));

    private static HostUserListRow ReadHostUserListRow(DbDataReader reader)
    {
        var row = new HostUserListRow
        {
            Id = ReadGuidByName(reader, "Id"),
            Username = ReadStringByName(reader, "Username"),
            DisplayName = ReadStringByName(reader, "DisplayName"),
            IsActive = ReadBooleanByName(reader, "IsActive"),
            CreatedAtUtc = ReadDateTimeOffsetByName(reader, "CreatedAtUtc"),
            UpdatedAtUtc = ReadNullableDateTimeOffsetByName(reader, "UpdatedAtUtc"),
            Version = ReadInt32ByName(reader, "Version"),
        };
        if (TryOrdinal(reader, "AccountType", out var accountType))
        {
            row.AccountType = reader.GetString(accountType);
        }

        return row;
    }

    private static HostRoleMemberRow ReadHostRoleMemberRow(DbDataReader reader) =>
        new()
        {
            UserId = ReadGuidByName(reader, "UserId"),
            Username = ReadStringByName(reader, "Username"),
            DisplayName = ReadStringByName(reader, "DisplayName"),
            IsActive = ReadBooleanByName(reader, "IsActive"),
        };

    private static HostUserPreferredLocaleRow ReadHostUserPreferredLocaleRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            Value = AotDataReaderExtensions.ReadNullableString(reader, 1),
        };

    private static HostUserFailedLoginCountRow ReadHostUserFailedLoginCountRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            Value = AotDataReaderExtensions.ReadInt32(reader, 1),
        };

    private static HostUserLockoutEndUtcRow ReadHostUserLockoutEndUtcRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            Value = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 1),
        };

    private static HostUserProfileRecord ReadHostUserProfileRecord(DbDataReader reader) =>
        new()
        {
            UserId = ReadGuidByName(reader, "UserId"),
            Nickname = ReadOptionalStringByName(reader, "Nickname"),
            PhoneNumber = ReadOptionalStringByName(reader, "PhoneNumber"),
            Email = ReadOptionalStringByName(reader, "Email"),
            EmployeeNumber = ReadOptionalStringByName(reader, "EmployeeNumber"),
            Gender = ReadOptionalStringByName(reader, "Gender"),
            JoinDateUtc = ReadOptionalDateTimeByName(reader, "JoinDateUtc"),
            SortOrder = TryOrdinal(reader, "SortOrder", out var sortOrder)
                ? AotDataReaderExtensions.ReadInt32(reader, sortOrder)
                : 0,
            IdCardType = ReadOptionalStringByName(reader, "IdCardType"),
            IdCardNumber = ReadOptionalStringByName(reader, "IdCardNumber"),
            BirthDate = ReadOptionalDateTimeByName(reader, "BirthDate"),
            Ethnicity = ReadOptionalStringByName(reader, "Ethnicity"),
            Address = ReadOptionalStringByName(reader, "Address"),
            GraduatedSchool = ReadOptionalStringByName(reader, "GraduatedSchool"),
            EducationLevel = ReadOptionalStringByName(reader, "EducationLevel"),
            PoliticalStatus = ReadOptionalStringByName(reader, "PoliticalStatus"),
            OfficePhone = ReadOptionalStringByName(reader, "OfficePhone"),
            EmergencyContact = ReadOptionalStringByName(reader, "EmergencyContact"),
            EmergencyContactRelation = ReadOptionalStringByName(reader, "EmergencyContactRelation"),
            EmergencyContactPhone = ReadOptionalStringByName(reader, "EmergencyContactPhone"),
            EmergencyContactAddress = ReadOptionalStringByName(reader, "EmergencyContactAddress"),
            Remark = ReadOptionalStringByName(reader, "Remark"),
            AvatarFileId = ReadOptionalGuidByName(reader, "AvatarFileId"),
            SignatureFileId = ReadOptionalGuidByName(reader, "SignatureFileId"),
            Version = ReadInt32ByName(reader, "Version"),
        };

    private static HostRoleListRow ReadHostRoleListRow(DbDataReader reader) =>
        new()
        {
            Id = ReadGuidByName(reader, "Id"),
            Code = ReadStringByName(reader, "Code"),
            Name = ReadStringByName(reader, "Name"),
            IsSystem = ReadBooleanByName(reader, "IsSystem"),
            IsActive = ReadBooleanByName(reader, "IsActive"),
            IsSuperAdministrator = ReadBooleanByName(reader, "IsSuperAdministrator"),
            CreatedAtUtc = ReadDateTimeOffsetByName(reader, "CreatedAtUtc"),
            UpdatedAtUtc = ReadNullableDateTimeOffsetByName(reader, "UpdatedAtUtc"),
            Version = ReadInt32ByName(reader, "Version"),
        };

    private static IdentityRoleRecord ReadIdentityRoleRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            AotDataReaderExtensions.ReadBoolean(reader, 6),
            AotDataReaderExtensions.ReadBoolean(reader, 7),
            reader.GetString(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            AotDataReaderExtensions.ReadInt32(reader, 11));

    private static IdentityRolePermission ReadIdentityRolePermission(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1));

    private static IdentityUserRoleDataScopeRow ReadIdentityUserRoleDataScopeRow(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            AotDataReaderExtensions.ReadBoolean(reader, 2));

    private static IdentityNavigationRecord ReadIdentityNavigationRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableGuid(reader, 3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            AotDataReaderExtensions.ReadInt32(reader, 10),
            reader.GetString(11),
            AotDataReaderExtensions.ReadBoolean(reader, 12),
            AotDataReaderExtensions.ReadBoolean(reader, 13),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 14),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 15),
            AotDataReaderExtensions.ReadInt32(reader, 16),
            reader.GetString(17),
            AotDataReaderExtensions.ReadNullableString(reader, 18),
            AotDataReaderExtensions.ReadNullableString(reader, 19),
            AotDataReaderExtensions.ReadBoolean(reader, 20),
            AotDataReaderExtensions.ReadBoolean(reader, 21),
            AotDataReaderExtensions.ReadBoolean(reader, 22),
            AotDataReaderExtensions.ReadBoolean(reader, 23),
            AotDataReaderExtensions.ReadNullableString(reader, 24));

    private static HostMenuListRow ReadHostMenuListRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            RouteName = reader.GetString(2),
            Path = reader.GetString(3),
            ComponentKey = reader.GetString(4),
            Title = reader.GetString(5),
            Caption = reader.GetString(6),
            Icon = reader.GetString(7),
            DisplayOrder = AotDataReaderExtensions.ReadInt32(reader, 8),
            RequiredPermission = reader.GetString(9),
            IsSystem = AotDataReaderExtensions.ReadBoolean(reader, 10),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 11),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            Version = AotDataReaderExtensions.ReadInt32(reader, 14),
            MenuType = reader.GetString(15),
            Redirect = AotDataReaderExtensions.ReadNullableString(reader, 16),
            LinkUrl = AotDataReaderExtensions.ReadNullableString(reader, 17),
            IsHidden = AotDataReaderExtensions.ReadBoolean(reader, 18),
            IsKeepAlive = AotDataReaderExtensions.ReadBoolean(reader, 19),
            IsAffix = AotDataReaderExtensions.ReadBoolean(reader, 20),
            IsEmbedded = AotDataReaderExtensions.ReadBoolean(reader, 21),
            Remark = AotDataReaderExtensions.ReadNullableString(reader, 22),
        };

    private static HostNavigationCatalogSyncService.HostMenuSyncRow ReadHostMenuSyncRow(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            RouteName = reader.GetString(2),
            MenuType = reader.GetString(3),
        };

    private static HostNavigationCatalogSyncService.HostMenuRouteNameRow ReadHostMenuRouteNameRow(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            RouteName = reader.GetString(1),
        };

    private static OnlineSessionListRow ReadOnlineSessionListRow(DbDataReader reader) =>
        new()
        {
            SessionId = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            Username = reader.GetString(2),
            DisplayName = reader.GetString(3),
            ClientId = reader.GetString(4),
            ActiveTenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            ExpiresAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
        };

    private static OnlineSessionRevokeRow ReadOnlineSessionRevokeRow(DbDataReader reader) =>
        new()
        {
            SessionId = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            FamilyId = reader.GetGuid(2),
            Username = reader.GetString(3),
            DisplayName = reader.GetString(4),
            ClientId = reader.GetString(5),
            ActiveTenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 6),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            ExpiresAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
        };

    private static ApiKeyListRow ReadApiKeyListRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            Username = reader.GetString(2),
            DisplayName = reader.GetString(3),
            KeyPrefix = reader.GetString(4),
            PermissionsJson = reader.GetString(5),
            ExpiresAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 7),
            LastUsedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
        };

    private static ApiKeyAuthenticationRow ReadApiKeyAuthenticationRow(DbDataReader reader) =>
        new()
        {
            ApiKeyId = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            Username = reader.GetString(2),
            DisplayName = reader.GetString(3),
            KeyPrefix = reader.GetString(4),
            KeyHash = reader.GetString(5),
            ScopeKey = reader.GetString(6),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            PermissionsJson = reader.GetString(8),
            ExpiresAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 10),
            LastUsedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            SecurityStamp = reader.GetString(12),
            UserIsActive = AotDataReaderExtensions.ReadBoolean(reader, 13),
            UserLockoutEndUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14),
        };

    private static OpenAccessClientDetailRow ReadOpenAccessClientDetailRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ApiKeyId = reader.GetGuid(1),
            Name = reader.GetString(2),
            Description = AotDataReaderExtensions.ReadNullableString(reader, 3),
            Remark = AotDataReaderExtensions.ReadNullableString(reader, 4),
            UserId = reader.GetGuid(5),
            Username = reader.GetString(6),
            AccessKeyId = reader.GetString(7),
            PermissionsJson = reader.GetString(8),
            ExpiresAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 10),
            LastUsedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            Version = AotDataReaderExtensions.ReadInt32(reader, 13),
            DailyRequestQuota = reader.IsDBNull(14) ? null : reader.GetInt32(14),
        };

    private static OpenAccessClientAccessLogRow ReadOpenAccessClientAccessLogRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            EventType = reader.GetString(1),
            ResultCode = reader.GetString(2),
            Succeeded = AotDataReaderExtensions.ReadBoolean(reader, 3),
            IpAddress = AotDataReaderExtensions.ReadNullableString(reader, 4),
            UserAgent = AotDataReaderExtensions.ReadNullableString(reader, 5),
            OccurredAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
        };

    private static OpenAccessClientAccessKeyRow ReadOpenAccessClientAccessKeyRow(DbDataReader reader) =>
        new()
        {
            ClientId = reader.GetGuid(0),
            AccessKeyId = reader.GetString(1),
        };

    private static OpenAccessClientQuotaRow ReadOpenAccessClientQuotaRow(DbDataReader reader) =>
        new()
        {
            ClientId = reader.GetGuid(0),
            DailyRequestQuota = reader.IsDBNull(1) ? null : reader.GetInt32(1),
            AccessKeyId = reader.GetString(2),
        };

    private static OpenAccessClientUsageCountRow ReadOpenAccessClientUsageCountRow(DbDataReader reader) =>
        new()
        {
            SuccessCount = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            FailureCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
        };

    private static RegistrationPolicyRecord ReadRegistrationPolicyRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadBoolean(reader, 1),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 2),
            AotDataReaderExtensions.ReadInt32(reader, 3));

    private static RegistrationWayRecord ReadRegistrationWayRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetGuid(1),
            Name = reader.GetString(2),
            Code = reader.GetString(3),
            IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 4),
            RoleId = reader.GetGuid(5),
            OrganizationUnitId = reader.GetGuid(6),
            PositionId = AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            SortOrder = AotDataReaderExtensions.ReadInt32(reader, 8),
            Remark = AotDataReaderExtensions.ReadNullableString(reader, 9),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
            Version = AotDataReaderExtensions.ReadInt32(reader, 12),
        };

    private static void BindRegistrationWayRecord(
        DbCommand command,
        RegistrationWayRecord value)
    {
        command.Parameters.AddWithValue("@Id", value.Id);
        command.Parameters.AddWithValue("@TenantId", value.TenantId);
        command.Parameters.AddWithValue("@Name", value.Name);
        command.Parameters.AddWithValue("@Code", value.Code);
        command.Parameters.AddWithValue("@IsEnabled", value.IsEnabled);
        command.Parameters.AddWithValue("@RoleId", value.RoleId);
        command.Parameters.AddWithValue("@OrganizationUnitId", value.OrganizationUnitId);
        command.Parameters.AddWithValue(
            "@PositionId",
            value.PositionId is null ? DBNull.Value : value.PositionId);
        command.Parameters.AddWithValue("@SortOrder", value.SortOrder);
        command.Parameters.AddWithValue(
            "@Remark",
            value.Remark is null ? DBNull.Value : value.Remark);
        command.Parameters.AddWithValue("@CreatedAtUtc", value.CreatedAtUtc);
        command.Parameters.AddWithValue(
            "@UpdatedAtUtc",
            value.UpdatedAtUtc is null ? DBNull.Value : value.UpdatedAtUtc);
        command.Parameters.AddWithValue("@Version", value.Version);
    }

    private static LdapConnectionRecord ReadLdapConnectionRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            Name = reader.GetString(2),
            Host = reader.GetString(3),
            Port = AotDataReaderExtensions.ReadInt32(reader, 4),
            UseTls = AotDataReaderExtensions.ReadBoolean(reader, 5),
            BaseDn = reader.GetString(6),
            BindDn = reader.GetString(7),
            BindPasswordProtected = reader.GetString(8),
            UserSearchFilter = reader.GetString(9),
            UserAccountAttribute = reader.GetString(10),
            EmployeeIdAttribute = AotDataReaderExtensions.ReadNullableString(reader, 11),
            DepartmentCodeAttribute = AotDataReaderExtensions.ReadNullableString(reader, 12),
            SyncSearchBaseDn = reader.GetString(13),
            IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 14),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 15),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 16),
            Version = AotDataReaderExtensions.ReadInt32(reader, 17),
        };

    private static void BindLdapConnectionRecord(
        DbCommand command,
        LdapConnectionRecord value)
    {
        command.Parameters.AddWithValue("@Id", value.Id);
        command.Parameters.AddWithValue(
            "@TenantId",
            value.TenantId is null ? DBNull.Value : value.TenantId);
        command.Parameters.AddWithValue("@Name", value.Name);
        command.Parameters.AddWithValue("@Host", value.Host);
        command.Parameters.AddWithValue("@Port", value.Port);
        command.Parameters.AddWithValue("@UseTls", value.UseTls);
        command.Parameters.AddWithValue("@BaseDn", value.BaseDn);
        command.Parameters.AddWithValue("@BindDn", value.BindDn);
        command.Parameters.AddWithValue("@BindPasswordProtected", value.BindPasswordProtected);
        command.Parameters.AddWithValue("@UserSearchFilter", value.UserSearchFilter);
        command.Parameters.AddWithValue("@UserAccountAttribute", value.UserAccountAttribute);
        command.Parameters.AddWithValue(
            "@EmployeeIdAttribute",
            value.EmployeeIdAttribute is null ? DBNull.Value : value.EmployeeIdAttribute);
        command.Parameters.AddWithValue(
            "@DepartmentCodeAttribute",
            value.DepartmentCodeAttribute is null ? DBNull.Value : value.DepartmentCodeAttribute);
        command.Parameters.AddWithValue("@SyncSearchBaseDn", value.SyncSearchBaseDn);
        command.Parameters.AddWithValue("@IsEnabled", value.IsEnabled);
        command.Parameters.AddWithValue("@CreatedAtUtc", value.CreatedAtUtc);
        command.Parameters.AddWithValue(
            "@UpdatedAtUtc",
            value.UpdatedAtUtc is null ? DBNull.Value : value.UpdatedAtUtc);
        command.Parameters.AddWithValue("@Version", value.Version);
    }

    private static OAuthProviderRecord ReadOAuthProviderRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ProviderKey = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Authority = reader.GetString(3),
            ClientId = reader.GetString(4),
            ClientSecretProtected = reader.GetString(5),
            Scopes = reader.GetString(6),
            RedirectPath = reader.GetString(7),
            IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 8),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            Version = AotDataReaderExtensions.ReadInt32(reader, 11),
        };

    private static void BindOAuthProviderRecord(
        DbCommand command,
        OAuthProviderRecord value)
    {
        command.Parameters.AddWithValue("@Id", value.Id);
        command.Parameters.AddWithValue("@ProviderKey", value.ProviderKey);
        command.Parameters.AddWithValue("@DisplayName", value.DisplayName);
        command.Parameters.AddWithValue("@Authority", value.Authority);
        command.Parameters.AddWithValue("@ClientId", value.ClientId);
        command.Parameters.AddWithValue("@ClientSecretProtected", value.ClientSecretProtected);
        command.Parameters.AddWithValue("@Scopes", value.Scopes);
        command.Parameters.AddWithValue("@RedirectPath", value.RedirectPath);
        command.Parameters.AddWithValue("@IsEnabled", value.IsEnabled);
        command.Parameters.AddWithValue("@CreatedAtUtc", value.CreatedAtUtc);
        command.Parameters.AddWithValue(
            "@UpdatedAtUtc",
            value.UpdatedAtUtc is null ? DBNull.Value : value.UpdatedAtUtc);
        command.Parameters.AddWithValue("@Version", value.Version);
    }

    private static OAuthPublicProviderRecord ReadOAuthPublicProviderRecord(DbDataReader reader) =>
        new()
        {
            ProviderKey = reader.GetString(0),
            DisplayName = reader.GetString(1),
        };

    private static OAuthUserLinkRecord ReadOAuthUserLinkRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            ProviderKey = reader.GetString(2),
            Subject = reader.GetString(3),
            Email = AotDataReaderExtensions.ReadNullableString(reader, 4),
            EmailVerified = AotDataReaderExtensions.ReadBoolean(reader, 5),
            DisplayName = AotDataReaderExtensions.ReadNullableString(reader, 6),
            LinkedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            LastUsedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            Version = AotDataReaderExtensions.ReadInt32(reader, 9),
        };

    private static OAuthUserLinkWithProviderRecord ReadOAuthUserLinkWithProviderRecord(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            ProviderKey = reader.GetString(2),
            Subject = reader.GetString(3),
            Email = AotDataReaderExtensions.ReadNullableString(reader, 4),
            EmailVerified = AotDataReaderExtensions.ReadBoolean(reader, 5),
            DisplayName = AotDataReaderExtensions.ReadNullableString(reader, 6),
            LinkedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            LastUsedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            Version = AotDataReaderExtensions.ReadInt32(reader, 9),
            ProviderDisplayName = reader.GetString(10),
        };

    private static void BindOAuthUserLinkRecord(
        DbCommand command,
        OAuthUserLinkRecord value)
    {
        command.Parameters.AddWithValue("@Id", value.Id);
        command.Parameters.AddWithValue("@UserId", value.UserId);
        command.Parameters.AddWithValue("@ProviderKey", value.ProviderKey);
        command.Parameters.AddWithValue("@Subject", value.Subject);
        command.Parameters.AddWithValue("@Email", value.Email is null ? DBNull.Value : value.Email);
        command.Parameters.AddWithValue("@EmailVerified", value.EmailVerified);
        command.Parameters.AddWithValue(
            "@DisplayName",
            value.DisplayName is null ? DBNull.Value : value.DisplayName);
        command.Parameters.AddWithValue("@LinkedAtUtc", value.LinkedAtUtc);
        command.Parameters.AddWithValue(
            "@LastUsedAtUtc",
            value.LastUsedAtUtc is null ? DBNull.Value : value.LastUsedAtUtc);
        command.Parameters.AddWithValue("@Version", value.Version);
    }

    private static OAuthAuthorizationStateRecord ReadOAuthAuthorizationStateRecord(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ProviderKey = reader.GetString(1),
            CodeVerifier = reader.GetString(2),
            Nonce = reader.GetString(3),
            Mode = reader.GetString(4),
            UserId = AotDataReaderExtensions.ReadNullableGuid(reader, 5),
            ReturnUrl = reader.GetString(6),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            ExpiresAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
        };

    private static void BindOAuthAuthorizationStateRecord(
        DbCommand command,
        OAuthAuthorizationStateRecord value)
    {
        command.Parameters.AddWithValue("@Id", value.Id);
        command.Parameters.AddWithValue("@ProviderKey", value.ProviderKey);
        command.Parameters.AddWithValue("@CodeVerifier", value.CodeVerifier);
        command.Parameters.AddWithValue("@Nonce", value.Nonce);
        command.Parameters.AddWithValue("@Mode", value.Mode);
        command.Parameters.AddWithValue(
            "@UserId",
            value.UserId is null ? DBNull.Value : value.UserId);
        command.Parameters.AddWithValue("@ReturnUrl", value.ReturnUrl);
        command.Parameters.AddWithValue("@CreatedAtUtc", value.CreatedAtUtc);
        command.Parameters.AddWithValue("@ExpiresAtUtc", value.ExpiresAtUtc);
    }

    private static IdentityUserTotpRecord ReadIdentityUserTotpRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            AotDataReaderExtensions.ReadBoolean(reader, 2),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 3),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 4),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadInt32(reader, 6));

    private static OrganizationUnitProjectionRecord ReadOrganizationUnitProjectionRecord(
        DbDataReader reader) =>
        new()
        {
            UnitId = reader.GetGuid(0),
            Name = reader.GetString(1),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 2),
            SourceVersion = AotDataReaderExtensions.ReadInt64(reader, 3),
        };

    private static UserFieldProjectionGrantRow ReadUserFieldProjectionGrantRow(
        DbDataReader reader) =>
        new(
            reader.GetString(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            AotDataReaderExtensions.ReadBoolean(reader, 2),
            AotDataReaderExtensions.ReadNullableString(reader, 3));

    private static IdentityRoleFieldGrantRow ReadIdentityRoleFieldGrantRow(
        DbDataReader reader) =>
        new(
            ReadStringByName(reader, "ResourceKey"),
            ReadStringByName(reader, "FieldKey"));

    private static SuperAdministratorResponse ReadSuperAdministratorResponse(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadBoolean(reader, 3));

    private static SuperAdministratorAuditResponse ReadSuperAdministratorAuditResponse(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            reader.GetString(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6));

    private static DynamicParameters BindLoginFailureUpdate(LoginFailureUpdate update)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", update.Id);
        parameters.Add("FailedLoginCount", update.FailedLoginCount);
        parameters.Add("LockoutEndUtc", update.LockoutEndUtc);
        parameters.Add("UpdatedAtUtc", update.UpdatedAtUtc);
        parameters.Add("Version", update.Version);
        return parameters;
    }

    private static DynamicParameters BindLoginSuccessUpdate(LoginSuccessUpdate update)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", update.Id);
        parameters.Add("PasswordHash", update.PasswordHash);
        parameters.Add("UpdatedAtUtc", update.UpdatedAtUtc);
        parameters.Add("Version", update.Version);
        return parameters;
    }

    private static DynamicParameters BindAuthAuditEvent(AuthAuditEvent audit)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", audit.Id);
        parameters.Add("UserId", audit.UserId);
        parameters.Add("SessionId", audit.SessionId);
        parameters.Add("UsernameFingerprint", audit.UsernameFingerprint);
        parameters.Add("EventType", audit.EventType);
        parameters.Add("ResultCode", audit.ResultCode);
        parameters.Add("Succeeded", audit.Succeeded);
        parameters.Add("IpAddress", audit.IpAddress);
        parameters.Add("UserAgent", audit.UserAgent);
        parameters.Add("ContextTenantId", audit.ContextTenantId);
        parameters.Add("OccurredAtUtc", audit.OccurredAtUtc);
        return parameters;
    }

    private static DynamicParameters BindRefreshSession(RefreshSession session)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", session.Id);
        parameters.Add("UserId", session.UserId);
        parameters.Add("FamilyId", session.FamilyId);
        parameters.Add("ClientId", session.ClientId);
        parameters.Add("TokenHash", session.TokenHash);
        parameters.Add("ExpiresAtUtc", session.ExpiresAtUtc);
        parameters.Add("ConsumedAtUtc", session.ConsumedAtUtc);
        parameters.Add("RevokedAtUtc", session.RevokedAtUtc);
        parameters.Add("ReplacedById", session.ReplacedById);
        parameters.Add("ActiveTenantId", session.ActiveTenantId);
        parameters.Add("CreatedAtUtc", session.CreatedAtUtc);
        parameters.Add("Version", session.Version);
        return parameters;
    }

    /// <summary>绑定刷新会话上下文的并发比较与目标值参数。</summary>
    /// <param name="update">包含令牌原上下文、目标上下文和会话版本的更新参数。</param>
    /// <returns>供 Dapper AOT 执行更新语句的固定参数集合。</returns>
    private static DynamicParameters BindRefreshSessionContextUpdate(
        Features.ChangeSessionContext.RefreshSessionContextUpdate update)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SessionId", update.SessionId);
        parameters.Add("UserId", update.UserId);
        parameters.Add("ActiveTenantId", update.ActiveTenantId);
        parameters.Add("ExpectedActiveTenantId", update.ExpectedActiveTenantId);
        parameters.Add("Version", update.Version);
        return parameters;
    }

    private static DynamicParameters BindIdentityUserRecord(IdentityUserRecord record)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", record.Id);
        parameters.Add("TenantId", record.TenantId);
        parameters.Add("ScopeKey", record.ScopeKey);
        parameters.Add("Username", record.Username);
        parameters.Add("NormalizedUsername", record.NormalizedUsername);
        parameters.Add("DisplayName", record.DisplayName);
        parameters.Add("PasswordHash", record.PasswordHash);
        parameters.Add("IsActive", record.IsActive);
        parameters.Add("FailedLoginCount", record.FailedLoginCount);
        parameters.Add("SecurityStamp", record.SecurityStamp);
        parameters.Add("CreatedAtUtc", record.CreatedAtUtc);
        parameters.Add("Version", record.Version);
        parameters.Add("PreferredLocale", record.PreferredLocale);
        parameters.Add("ProfileVersion", record.ProfileVersion);
        parameters.Add("AccountType", record.AccountType);
        parameters.Add("MustChangePassword", record.MustChangePassword);
        parameters.Add("PasswordChangedAtUtc", record.PasswordChangedAtUtc);
        return parameters;
    }

    private static DynamicParameters BindInsertIdentityRole(InsertIdentityRole role)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", role.Id);
        parameters.Add("TenantId", role.TenantId);
        parameters.Add("ScopeKey", role.ScopeKey);
        parameters.Add("Code", role.Code);
        parameters.Add("Name", role.Name);
        parameters.Add("IsSystem", role.IsSystem);
        parameters.Add("IsActive", role.IsActive);
        parameters.Add("IsSuperAdministrator", role.IsSuperAdministrator);
        parameters.Add("DataScopeKind", role.DataScopeKind);
        parameters.Add("CreatedAtUtc", role.CreatedAtUtc);
        parameters.Add("Version", role.Version);
        return parameters;
    }

    private static DynamicParameters BindInsertIdentityNavigation(InsertIdentityNavigation menu)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", menu.Id);
        parameters.Add("TenantId", menu.TenantId);
        parameters.Add("ScopeKey", menu.ScopeKey);
        parameters.Add("ParentId", menu.ParentId);
        parameters.Add("RouteName", menu.RouteName);
        parameters.Add("Path", menu.Path);
        parameters.Add("ComponentKey", menu.ComponentKey);
        parameters.Add("Title", menu.Title);
        parameters.Add("Caption", menu.Caption);
        parameters.Add("Icon", menu.Icon);
        parameters.Add("DisplayOrder", menu.DisplayOrder);
        parameters.Add("RequiredPermission", menu.RequiredPermission);
        parameters.Add("IsSystem", menu.IsSystem);
        parameters.Add("IsActive", menu.IsActive);
        parameters.Add("CreatedAtUtc", menu.CreatedAtUtc);
        parameters.Add("Version", menu.Version);
        parameters.Add("MenuType", menu.MenuType);
        parameters.Add("Redirect", menu.Redirect);
        parameters.Add("LinkUrl", menu.LinkUrl);
        parameters.Add("IsHidden", menu.IsHidden);
        parameters.Add("IsKeepAlive", menu.IsKeepAlive);
        parameters.Add("IsAffix", menu.IsAffix);
        parameters.Add("IsEmbedded", menu.IsEmbedded);
        parameters.Add("Remark", menu.Remark);
        return parameters;
    }

    private static bool TryOrdinal(DbDataReader reader, string name, out int ordinal)
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (string.Equals(reader.GetName(index), name, StringComparison.OrdinalIgnoreCase))
            {
                ordinal = index;
                return true;
            }
        }

        ordinal = -1;
        return false;
    }

    private static int RequiredOrdinal(DbDataReader reader, string name)
    {
        if (!TryOrdinal(reader, name, out var ordinal))
        {
            throw new InvalidOperationException($"查询结果缺少列 {name}。");
        }

        return ordinal;
    }

    private static Guid ReadGuidByName(DbDataReader reader, string name) =>
        reader.GetGuid(RequiredOrdinal(reader, name));

    private static Guid? ReadOptionalGuidByName(DbDataReader reader, string name)
    {
        if (!TryOrdinal(reader, name, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetGuid(ordinal);
    }

    private static string ReadStringByName(DbDataReader reader, string name) =>
        reader.GetString(RequiredOrdinal(reader, name));

    private static string? ReadOptionalStringByName(DbDataReader reader, string name)
    {
        if (!TryOrdinal(reader, name, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetString(ordinal);
    }

    private static bool ReadBooleanByName(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadBoolean(reader, RequiredOrdinal(reader, name));

    private static int ReadInt32ByName(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadInt32(reader, RequiredOrdinal(reader, name));

    private static DateTimeOffset ReadDateTimeOffsetByName(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadDateTimeOffset(reader, RequiredOrdinal(reader, name));

    private static DateTimeOffset? ReadNullableDateTimeOffsetByName(
        DbDataReader reader,
        string name)
    {
        if (!TryOrdinal(reader, name, out var ordinal))
        {
            return null;
        }

        return AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, ordinal);
    }

    private static DateTime? ReadOptionalDateTimeByName(DbDataReader reader, string name)
    {
        if (!TryOrdinal(reader, name, out var ordinal) || reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetDateTime(ordinal);
    }
}
#endif
