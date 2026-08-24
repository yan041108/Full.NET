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

        DapperAotParameterRegistry.Register<LoginFailureUpdate>(BindLoginFailureUpdate);
        DapperAotParameterRegistry.Register<LoginSuccessUpdate>(BindLoginSuccessUpdate);
        DapperAotParameterRegistry.Register<AuthAuditEvent>(BindAuthAuditEvent);
        DapperAotParameterRegistry.Register<RefreshSession>(BindRefreshSession);
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
            reader.GetBoolean(7),
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
            reader.GetBoolean(1));

    private static IdentityProfileRecord ReadIdentityProfileRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ScopeKey = reader.GetString(1),
            Username = reader.GetString(2),
            DisplayName = reader.GetString(3),
            IsActive = reader.GetBoolean(4),
            PreferredLocale = reader.GetString(5),
            ProfileVersion = reader.GetInt32(6),
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
}
#endif
