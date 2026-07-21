-- 合法：写操作带 WHERE；非破坏性 DDL。
UPDATE fn_identity_user
SET DisplayName = @DisplayName
WHERE Id = @UserId;

DELETE FROM fn_identity_user_role
WHERE UserId = @UserId AND RoleId = @RoleId;

ALTER TABLE fn_identity_role
    ADD DataScopeKind varchar(64) NOT NULL;
