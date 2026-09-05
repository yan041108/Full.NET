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
        registrar.Register<HostUserListRow>(ReadHostUserListRow);
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
        registrar.Register<IdentityUserTotpRecord>(ReadIdentityUserTotpRecord);
        registrar.Register<OrganizationUnitProjectionRecord>(ReadOrganizationUnitProjectionRecord);
        registrar.Register<UserFieldProjectionGrantRow>(ReadUserFieldProjectionGrantRow);
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
            reader.GetString(16));

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
        };

    private static HostUserDirectoryRecord ReadHostUserDirectoryRecord(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));

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
