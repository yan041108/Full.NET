-- 只返回聚合计数和 SHA-256，不输出 UUID 或业务行；任一 mismatch/missing count 非零都必须停止。
-- 哈希样本由 UUID 文本 CRC32 模 97 固定选择，可跨全表稳定复核而不只覆盖首尾行。
SET SESSION group_concat_max_len = 16777216;

SELECT 'fn_tenant_tenant' AS TableName,
       'Id' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`Id`) AS SourceNonNullCount,
       COUNT(`IdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`Id`)) AS SourceDistinctCount,
       COUNT(DISTINCT `IdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`Id` IS NOT NULL AND BIN_TO_UUID(`IdBinary`, 0) <> LOWER(`Id`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0
                THEN CONCAT(LOWER(`Id`), '=', HEX(`IdBinary`)) END
           ORDER BY LOWER(`Id`), HEX(`IdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_tenant_tenant`
UNION ALL
SELECT 'fn_outbox_message' AS TableName,
       'Id' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`Id`) AS SourceNonNullCount,
       COUNT(`IdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`Id`)) AS SourceDistinctCount,
       COUNT(DISTINCT `IdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`Id` IS NOT NULL AND BIN_TO_UUID(`IdBinary`, 0) <> LOWER(`Id`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0
                THEN CONCAT(LOWER(`Id`), '=', HEX(`IdBinary`)) END
           ORDER BY LOWER(`Id`), HEX(`IdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_outbox_message`
UNION ALL
SELECT 'fn_outbox_message' AS TableName,
       'TenantId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`TenantId`) AS SourceNonNullCount,
       COUNT(`TenantIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`TenantId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `TenantIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`TenantId` IS NOT NULL AND BIN_TO_UUID(`TenantIdBinary`, 0) <> LOWER(`TenantId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`TenantId` IS NOT NULL AND MOD(CRC32(LOWER(`TenantId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `TenantId` IS NOT NULL AND MOD(CRC32(LOWER(`TenantId`)), 97) = 0
                THEN CONCAT(LOWER(`TenantId`), '=', HEX(`TenantIdBinary`)) END
           ORDER BY LOWER(`TenantId`), HEX(`TenantIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_outbox_message`
UNION ALL
SELECT 'fn_outbox_message' AS TableName,
       'LockId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`LockId`) AS SourceNonNullCount,
       COUNT(`LockIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`LockId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `LockIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`LockId` IS NOT NULL AND BIN_TO_UUID(`LockIdBinary`, 0) <> LOWER(`LockId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`LockId` IS NOT NULL AND MOD(CRC32(LOWER(`LockId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `LockId` IS NOT NULL AND MOD(CRC32(LOWER(`LockId`)), 97) = 0
                THEN CONCAT(LOWER(`LockId`), '=', HEX(`LockIdBinary`)) END
           ORDER BY LOWER(`LockId`), HEX(`LockIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_outbox_message`
UNION ALL
SELECT 'fn_identity_user' AS TableName,
       'Id' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`Id`) AS SourceNonNullCount,
       COUNT(`IdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`Id`)) AS SourceDistinctCount,
       COUNT(DISTINCT `IdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`Id` IS NOT NULL AND BIN_TO_UUID(`IdBinary`, 0) <> LOWER(`Id`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0
                THEN CONCAT(LOWER(`Id`), '=', HEX(`IdBinary`)) END
           ORDER BY LOWER(`Id`), HEX(`IdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_user`
UNION ALL
SELECT 'fn_identity_user' AS TableName,
       'TenantId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`TenantId`) AS SourceNonNullCount,
       COUNT(`TenantIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`TenantId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `TenantIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`TenantId` IS NOT NULL AND BIN_TO_UUID(`TenantIdBinary`, 0) <> LOWER(`TenantId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`TenantId` IS NOT NULL AND MOD(CRC32(LOWER(`TenantId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `TenantId` IS NOT NULL AND MOD(CRC32(LOWER(`TenantId`)), 97) = 0
                THEN CONCAT(LOWER(`TenantId`), '=', HEX(`TenantIdBinary`)) END
           ORDER BY LOWER(`TenantId`), HEX(`TenantIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_user`
UNION ALL
SELECT 'fn_identity_refresh_session' AS TableName,
       'Id' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`Id`) AS SourceNonNullCount,
       COUNT(`IdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`Id`)) AS SourceDistinctCount,
       COUNT(DISTINCT `IdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`Id` IS NOT NULL AND BIN_TO_UUID(`IdBinary`, 0) <> LOWER(`Id`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0
                THEN CONCAT(LOWER(`Id`), '=', HEX(`IdBinary`)) END
           ORDER BY LOWER(`Id`), HEX(`IdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_refresh_session`
UNION ALL
SELECT 'fn_identity_refresh_session' AS TableName,
       'UserId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`UserId`) AS SourceNonNullCount,
       COUNT(`UserIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`UserId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `UserIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`UserId` IS NOT NULL AND BIN_TO_UUID(`UserIdBinary`, 0) <> LOWER(`UserId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`UserId` IS NOT NULL AND MOD(CRC32(LOWER(`UserId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `UserId` IS NOT NULL AND MOD(CRC32(LOWER(`UserId`)), 97) = 0
                THEN CONCAT(LOWER(`UserId`), '=', HEX(`UserIdBinary`)) END
           ORDER BY LOWER(`UserId`), HEX(`UserIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_refresh_session`
UNION ALL
SELECT 'fn_identity_refresh_session' AS TableName,
       'FamilyId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`FamilyId`) AS SourceNonNullCount,
       COUNT(`FamilyIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`FamilyId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `FamilyIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`FamilyId` IS NOT NULL AND BIN_TO_UUID(`FamilyIdBinary`, 0) <> LOWER(`FamilyId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`FamilyId` IS NOT NULL AND MOD(CRC32(LOWER(`FamilyId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `FamilyId` IS NOT NULL AND MOD(CRC32(LOWER(`FamilyId`)), 97) = 0
                THEN CONCAT(LOWER(`FamilyId`), '=', HEX(`FamilyIdBinary`)) END
           ORDER BY LOWER(`FamilyId`), HEX(`FamilyIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_refresh_session`
UNION ALL
SELECT 'fn_identity_refresh_session' AS TableName,
       'ReplacedById' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`ReplacedById`) AS SourceNonNullCount,
       COUNT(`ReplacedByIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`ReplacedById`)) AS SourceDistinctCount,
       COUNT(DISTINCT `ReplacedByIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`ReplacedById` IS NOT NULL AND BIN_TO_UUID(`ReplacedByIdBinary`, 0) <> LOWER(`ReplacedById`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`ReplacedById` IS NOT NULL AND MOD(CRC32(LOWER(`ReplacedById`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `ReplacedById` IS NOT NULL AND MOD(CRC32(LOWER(`ReplacedById`)), 97) = 0
                THEN CONCAT(LOWER(`ReplacedById`), '=', HEX(`ReplacedByIdBinary`)) END
           ORDER BY LOWER(`ReplacedById`), HEX(`ReplacedByIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_refresh_session`
UNION ALL
SELECT 'fn_identity_refresh_session' AS TableName,
       'ActiveTenantId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`ActiveTenantId`) AS SourceNonNullCount,
       COUNT(`ActiveTenantIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`ActiveTenantId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `ActiveTenantIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`ActiveTenantId` IS NOT NULL AND BIN_TO_UUID(`ActiveTenantIdBinary`, 0) <> LOWER(`ActiveTenantId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`ActiveTenantId` IS NOT NULL AND MOD(CRC32(LOWER(`ActiveTenantId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `ActiveTenantId` IS NOT NULL AND MOD(CRC32(LOWER(`ActiveTenantId`)), 97) = 0
                THEN CONCAT(LOWER(`ActiveTenantId`), '=', HEX(`ActiveTenantIdBinary`)) END
           ORDER BY LOWER(`ActiveTenantId`), HEX(`ActiveTenantIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_refresh_session`
UNION ALL
SELECT 'fn_identity_auth_audit' AS TableName,
       'Id' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`Id`) AS SourceNonNullCount,
       COUNT(`IdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`Id`)) AS SourceDistinctCount,
       COUNT(DISTINCT `IdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`Id` IS NOT NULL AND BIN_TO_UUID(`IdBinary`, 0) <> LOWER(`Id`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0
                THEN CONCAT(LOWER(`Id`), '=', HEX(`IdBinary`)) END
           ORDER BY LOWER(`Id`), HEX(`IdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_auth_audit`
UNION ALL
SELECT 'fn_identity_auth_audit' AS TableName,
       'UserId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`UserId`) AS SourceNonNullCount,
       COUNT(`UserIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`UserId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `UserIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`UserId` IS NOT NULL AND BIN_TO_UUID(`UserIdBinary`, 0) <> LOWER(`UserId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`UserId` IS NOT NULL AND MOD(CRC32(LOWER(`UserId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `UserId` IS NOT NULL AND MOD(CRC32(LOWER(`UserId`)), 97) = 0
                THEN CONCAT(LOWER(`UserId`), '=', HEX(`UserIdBinary`)) END
           ORDER BY LOWER(`UserId`), HEX(`UserIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_auth_audit`
UNION ALL
SELECT 'fn_identity_auth_audit' AS TableName,
       'SessionId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`SessionId`) AS SourceNonNullCount,
       COUNT(`SessionIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`SessionId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `SessionIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`SessionId` IS NOT NULL AND BIN_TO_UUID(`SessionIdBinary`, 0) <> LOWER(`SessionId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`SessionId` IS NOT NULL AND MOD(CRC32(LOWER(`SessionId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `SessionId` IS NOT NULL AND MOD(CRC32(LOWER(`SessionId`)), 97) = 0
                THEN CONCAT(LOWER(`SessionId`), '=', HEX(`SessionIdBinary`)) END
           ORDER BY LOWER(`SessionId`), HEX(`SessionIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_auth_audit`
UNION ALL
SELECT 'fn_identity_auth_audit' AS TableName,
       'ContextTenantId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`ContextTenantId`) AS SourceNonNullCount,
       COUNT(`ContextTenantIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`ContextTenantId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `ContextTenantIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`ContextTenantId` IS NOT NULL AND BIN_TO_UUID(`ContextTenantIdBinary`, 0) <> LOWER(`ContextTenantId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`ContextTenantId` IS NOT NULL AND MOD(CRC32(LOWER(`ContextTenantId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `ContextTenantId` IS NOT NULL AND MOD(CRC32(LOWER(`ContextTenantId`)), 97) = 0
                THEN CONCAT(LOWER(`ContextTenantId`), '=', HEX(`ContextTenantIdBinary`)) END
           ORDER BY LOWER(`ContextTenantId`), HEX(`ContextTenantIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_auth_audit`
UNION ALL
SELECT 'fn_identity_auth_audit' AS TableName,
       'ActorUserId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`ActorUserId`) AS SourceNonNullCount,
       COUNT(`ActorUserIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`ActorUserId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `ActorUserIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`ActorUserId` IS NOT NULL AND BIN_TO_UUID(`ActorUserIdBinary`, 0) <> LOWER(`ActorUserId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`ActorUserId` IS NOT NULL AND MOD(CRC32(LOWER(`ActorUserId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `ActorUserId` IS NOT NULL AND MOD(CRC32(LOWER(`ActorUserId`)), 97) = 0
                THEN CONCAT(LOWER(`ActorUserId`), '=', HEX(`ActorUserIdBinary`)) END
           ORDER BY LOWER(`ActorUserId`), HEX(`ActorUserIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_auth_audit`
UNION ALL
SELECT 'fn_identity_role' AS TableName,
       'Id' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`Id`) AS SourceNonNullCount,
       COUNT(`IdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`Id`)) AS SourceDistinctCount,
       COUNT(DISTINCT `IdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`Id` IS NOT NULL AND BIN_TO_UUID(`IdBinary`, 0) <> LOWER(`Id`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0
                THEN CONCAT(LOWER(`Id`), '=', HEX(`IdBinary`)) END
           ORDER BY LOWER(`Id`), HEX(`IdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_role`
UNION ALL
SELECT 'fn_identity_role' AS TableName,
       'TenantId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`TenantId`) AS SourceNonNullCount,
       COUNT(`TenantIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`TenantId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `TenantIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`TenantId` IS NOT NULL AND BIN_TO_UUID(`TenantIdBinary`, 0) <> LOWER(`TenantId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`TenantId` IS NOT NULL AND MOD(CRC32(LOWER(`TenantId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `TenantId` IS NOT NULL AND MOD(CRC32(LOWER(`TenantId`)), 97) = 0
                THEN CONCAT(LOWER(`TenantId`), '=', HEX(`TenantIdBinary`)) END
           ORDER BY LOWER(`TenantId`), HEX(`TenantIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_role`
UNION ALL
SELECT 'fn_identity_user_role' AS TableName,
       'UserId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`UserId`) AS SourceNonNullCount,
       COUNT(`UserIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`UserId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `UserIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`UserId` IS NOT NULL AND BIN_TO_UUID(`UserIdBinary`, 0) <> LOWER(`UserId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`UserId` IS NOT NULL AND MOD(CRC32(LOWER(`UserId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `UserId` IS NOT NULL AND MOD(CRC32(LOWER(`UserId`)), 97) = 0
                THEN CONCAT(LOWER(`UserId`), '=', HEX(`UserIdBinary`)) END
           ORDER BY LOWER(`UserId`), HEX(`UserIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_user_role`
UNION ALL
SELECT 'fn_identity_user_role' AS TableName,
       'RoleId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`RoleId`) AS SourceNonNullCount,
       COUNT(`RoleIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`RoleId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `RoleIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`RoleId` IS NOT NULL AND BIN_TO_UUID(`RoleIdBinary`, 0) <> LOWER(`RoleId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`RoleId` IS NOT NULL AND MOD(CRC32(LOWER(`RoleId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `RoleId` IS NOT NULL AND MOD(CRC32(LOWER(`RoleId`)), 97) = 0
                THEN CONCAT(LOWER(`RoleId`), '=', HEX(`RoleIdBinary`)) END
           ORDER BY LOWER(`RoleId`), HEX(`RoleIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_user_role`
UNION ALL
SELECT 'fn_identity_role_permission' AS TableName,
       'RoleId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`RoleId`) AS SourceNonNullCount,
       COUNT(`RoleIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`RoleId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `RoleIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`RoleId` IS NOT NULL AND BIN_TO_UUID(`RoleIdBinary`, 0) <> LOWER(`RoleId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`RoleId` IS NOT NULL AND MOD(CRC32(LOWER(`RoleId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `RoleId` IS NOT NULL AND MOD(CRC32(LOWER(`RoleId`)), 97) = 0
                THEN CONCAT(LOWER(`RoleId`), '=', HEX(`RoleIdBinary`)) END
           ORDER BY LOWER(`RoleId`), HEX(`RoleIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_identity_role_permission`
UNION ALL
SELECT 'fn_seed_run' AS TableName,
       'Id' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`Id`) AS SourceNonNullCount,
       COUNT(`IdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`Id`)) AS SourceDistinctCount,
       COUNT(DISTINCT `IdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`Id` IS NOT NULL AND BIN_TO_UUID(`IdBinary`, 0) <> LOWER(`Id`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `Id` IS NOT NULL AND MOD(CRC32(LOWER(`Id`)), 97) = 0
                THEN CONCAT(LOWER(`Id`), '=', HEX(`IdBinary`)) END
           ORDER BY LOWER(`Id`), HEX(`IdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_seed_run`
UNION ALL
SELECT 'fn_seed_run_item' AS TableName,
       'RunId' AS ColumnName,
       COUNT(*) AS RowCount,
       COUNT(`RunId`) AS SourceNonNullCount,
       COUNT(`RunIdBinary`) AS TargetNonNullCount,
       COUNT(DISTINCT LOWER(`RunId`)) AS SourceDistinctCount,
       COUNT(DISTINCT `RunIdBinary`) AS TargetDistinctCount,
       COALESCE(SUM(`RunId` IS NOT NULL AND BIN_TO_UUID(`RunIdBinary`, 0) <> LOWER(`RunId`)), 0) AS RoundTripMismatchCount,
       COALESCE(SUM(`RunId` IS NOT NULL AND MOD(CRC32(LOWER(`RunId`)), 97) = 0), 0) AS SampleCount,
       SHA2(COALESCE(GROUP_CONCAT(
           CASE WHEN `RunId` IS NOT NULL AND MOD(CRC32(LOWER(`RunId`)), 97) = 0
                THEN CONCAT(LOWER(`RunId`), '=', HEX(`RunIdBinary`)) END
           ORDER BY LOWER(`RunId`), HEX(`RunIdBinary`) SEPARATOR '|'), ''), 256) AS SampleSha256
FROM `fn_seed_run_item`;

-- 每行 MatchingUniqueIndexCount 必须为 1；完整 Binary16 单列唯一索引不允许前缀或附加列。
SELECT expected.TableName,
       expected.IndexName,
       CASE
           WHEN COUNT(actual.INDEX_NAME) = 1
            AND MAX(actual.NON_UNIQUE = 0
                    AND actual.COLUMN_NAME = expected.ColumnName
                    AND actual.SEQ_IN_INDEX = 1
                    AND actual.SUB_PART IS NULL) = 1
           THEN 1 ELSE 0
       END AS MatchingUniqueIndexCount
FROM (
    SELECT 'fn_tenant_tenant' AS TableName, 'UX_fn_tenant_tenant_IdBinary' AS IndexName, 'IdBinary' AS ColumnName
    UNION ALL SELECT 'fn_outbox_message', 'UX_fn_outbox_message_IdBinary', 'IdBinary'
    UNION ALL SELECT 'fn_identity_user', 'UX_fn_identity_user_IdBinary', 'IdBinary'
    UNION ALL SELECT 'fn_identity_refresh_session', 'UX_fn_identity_refresh_session_IdBinary', 'IdBinary'
    UNION ALL SELECT 'fn_identity_auth_audit', 'UX_fn_identity_auth_audit_IdBinary', 'IdBinary'
    UNION ALL SELECT 'fn_identity_role', 'UX_fn_identity_role_IdBinary', 'IdBinary'
    UNION ALL SELECT 'fn_seed_run', 'UX_fn_seed_run_IdBinary', 'IdBinary'
) AS expected
LEFT JOIN INFORMATION_SCHEMA.STATISTICS AS actual
 ON actual.TABLE_SCHEMA = DATABASE()
 AND actual.TABLE_NAME = expected.TableName
 AND actual.INDEX_NAME = expected.IndexName
GROUP BY expected.TableName, expected.IndexName, expected.ColumnName
ORDER BY expected.TableName;

-- 每行 MatchingTriggerCount 必须为 1；同时锁定固定名称、BEFORE 时机、事件和同步/拒绝正文。
SELECT expected.TriggerName,
       expected.EventName,
       COUNT(actual.TRIGGER_NAME) AS MatchingTriggerCount
FROM (
    SELECT 'TR_fn_tenant_tenant_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_tenant_tenant_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_outbox_message_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_outbox_message_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_identity_user_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_identity_user_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_identity_refresh_session_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_identity_refresh_session_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_identity_auth_audit_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_identity_auth_audit_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_identity_role_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_identity_role_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_identity_user_role_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_identity_user_role_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_identity_role_permission_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_identity_role_permission_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_seed_run_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_seed_run_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
    UNION ALL SELECT 'TR_fn_seed_run_item_UuidBinary_BI' AS TriggerName, 'INSERT' AS EventName
    UNION ALL SELECT 'TR_fn_seed_run_item_UuidBinary_BU' AS TriggerName, 'UPDATE' AS EventName
) AS expected
LEFT JOIN INFORMATION_SCHEMA.TRIGGERS AS actual
  ON actual.TRIGGER_SCHEMA = DATABASE()
 AND actual.TRIGGER_NAME = expected.TriggerName
 AND actual.ACTION_TIMING = 'BEFORE'
 AND actual.EVENT_MANIPULATION = expected.EventName
 AND actual.ACTION_STATEMENT LIKE '%UUID_TO_BIN%'
 AND actual.ACTION_STATEMENT LIKE '%SIGNAL SQLSTATE%'
GROUP BY expected.TriggerName, expected.EventName
ORDER BY expected.TriggerName;

SELECT 'fn_outbox_message.TenantId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_outbox_message` AS child
LEFT JOIN `fn_tenant_tenant` AS parent ON parent.`IdBinary` = child.`TenantIdBinary`
WHERE child.`TenantIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_user.TenantId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_user` AS child
LEFT JOIN `fn_tenant_tenant` AS parent ON parent.`IdBinary` = child.`TenantIdBinary`
WHERE child.`TenantIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_refresh_session.UserId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_refresh_session` AS child
LEFT JOIN `fn_identity_user` AS parent ON parent.`IdBinary` = child.`UserIdBinary`
WHERE child.`UserIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_refresh_session.ReplacedById' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_refresh_session` AS child
LEFT JOIN `fn_identity_refresh_session` AS parent ON parent.`IdBinary` = child.`ReplacedByIdBinary`
WHERE child.`ReplacedByIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_refresh_session.ActiveTenantId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_refresh_session` AS child
LEFT JOIN `fn_tenant_tenant` AS parent ON parent.`IdBinary` = child.`ActiveTenantIdBinary`
WHERE child.`ActiveTenantIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_auth_audit.UserId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_auth_audit` AS child
LEFT JOIN `fn_identity_user` AS parent ON parent.`IdBinary` = child.`UserIdBinary`
WHERE child.`UserIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_auth_audit.SessionId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_auth_audit` AS child
LEFT JOIN `fn_identity_refresh_session` AS parent ON parent.`IdBinary` = child.`SessionIdBinary`
WHERE child.`SessionIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_auth_audit.ContextTenantId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_auth_audit` AS child
LEFT JOIN `fn_tenant_tenant` AS parent ON parent.`IdBinary` = child.`ContextTenantIdBinary`
WHERE child.`ContextTenantIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_auth_audit.ActorUserId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_auth_audit` AS child
LEFT JOIN `fn_identity_user` AS parent ON parent.`IdBinary` = child.`ActorUserIdBinary`
WHERE child.`ActorUserIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_role.TenantId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_role` AS child
LEFT JOIN `fn_tenant_tenant` AS parent ON parent.`IdBinary` = child.`TenantIdBinary`
WHERE child.`TenantIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_user_role.UserId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_user_role` AS child
LEFT JOIN `fn_identity_user` AS parent ON parent.`IdBinary` = child.`UserIdBinary`
WHERE child.`UserIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_user_role.RoleId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_user_role` AS child
LEFT JOIN `fn_identity_role` AS parent ON parent.`IdBinary` = child.`RoleIdBinary`
WHERE child.`RoleIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_identity_role_permission.RoleId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_identity_role_permission` AS child
LEFT JOIN `fn_identity_role` AS parent ON parent.`IdBinary` = child.`RoleIdBinary`
WHERE child.`RoleIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL
UNION ALL
SELECT 'fn_seed_run_item.RunId' AS ReferenceName,
       COUNT(*) AS MissingReferenceCount
FROM `fn_seed_run_item` AS child
LEFT JOIN `fn_seed_run` AS parent ON parent.`IdBinary` = child.`RunIdBinary`
WHERE child.`RunIdBinary` IS NOT NULL AND parent.`IdBinary` IS NULL;
