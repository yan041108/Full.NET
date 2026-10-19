using Full.NET.Data.Abstractions;

/// <summary>Identity 模块显式 SQL 语句；参数化访问，作用域在声明处标注。</summary>
namespace Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes.Persistence;

internal static class MfaRecoveryCodeSql
{
    public static readonly SqlStatement DeleteByUserId = new(
        "identity.mfa_recovery_codes.delete_by_user",
        """
        DELETE FROM fn_identity_user_mfa_recovery_code
        WHERE UserId = @UserId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Insert = new(
        "identity.mfa_recovery_codes.insert",
        """
        INSERT INTO fn_identity_user_mfa_recovery_code
            (Id, UserId, CodeHash, ConsumedAtUtc, CreatedAtUtc, Version)
        VALUES
            (@Id, @UserId, @CodeHash, NULL, @CreatedAtUtc, 1)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListActiveByUserId = new(
        "identity.mfa_recovery_codes.list_active_by_user",
        """
        SELECT Id, CodeHash, Version
        FROM fn_identity_user_mfa_recovery_code
        WHERE UserId = @UserId
          AND ConsumedAtUtc IS NULL
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement Consume = new(
        "identity.mfa_recovery_codes.consume",
        """
        UPDATE fn_identity_user_mfa_recovery_code
        SET ConsumedAtUtc = @ConsumedAtUtc,
            Version = Version + 1
        WHERE Id = @Id
          AND ConsumedAtUtc IS NULL
          AND Version = @Version
        """,
        SqlDataScope.Global);
}
