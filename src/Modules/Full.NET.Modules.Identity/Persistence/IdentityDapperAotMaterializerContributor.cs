#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using global::Dapper;

namespace Full.NET.Modules.Identity.Persistence;

/// <summary>
/// Identity 模块 Native AOT Dapper 注册：行物化与参数绑定。
/// </summary>
internal sealed class IdentityDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<IdentityUserRecord>(ReadIdentityUserRecord);
        registrar.Register<IdentityAuthorizationRow>(ReadIdentityAuthorizationRow);
        registrar.Register<IdentityProfileRecord>(ReadIdentityProfileRecord);
        registrar.Register<RefreshSessionRecord>(ReadRefreshSessionRecord);

        DapperAotParameterRegistry.Register<LoginFailureUpdate>(BindLoginFailureUpdate);
        DapperAotParameterRegistry.Register<LoginSuccessUpdate>(BindLoginSuccessUpdate);
        DapperAotParameterRegistry.Register<AuthAuditEvent>(BindAuthAuditEvent);
        DapperAotParameterRegistry.Register<RefreshSession>(BindRefreshSession);
        DapperAotParameterRegistry.Register<Features.ChangeSessionContext.RefreshSessionContextUpdate>(
            BindRefreshSessionContextUpdate);
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
            reader.GetInt32(8),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            reader.GetString(10),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            reader.GetInt32(13),
            reader.GetString(14),
            reader.GetInt32(15),
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
            ProfileVersion = reader.GetInt32(6),
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
            SessionVersion = reader.GetInt32(11),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 12),
            ScopeKey = reader.GetString(13),
            Username = reader.GetString(14),
            NormalizedUsername = reader.GetString(15),
            DisplayName = reader.GetString(16),
            PasswordHash = reader.GetString(17),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 18),
            FailedLoginCount = reader.GetInt32(19),
            LockoutEndUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 20),
            SecurityStamp = reader.GetString(21),
            UserCreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 22),
            UserUpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 23),
            UserVersion = reader.GetInt32(24),
            PreferredLocale = reader.GetString(25),
            ProfileVersion = reader.GetInt32(26),
        };

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

    private static DynamicParameters BindRefreshSessionContextUpdate(
        Features.ChangeSessionContext.RefreshSessionContextUpdate update)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SessionId", update.SessionId);
        parameters.Add("UserId", update.UserId);
        parameters.Add("ActiveTenantId", update.ActiveTenantId);
        parameters.Add("Version", update.Version);
        return parameters;
    }
}
#endif
