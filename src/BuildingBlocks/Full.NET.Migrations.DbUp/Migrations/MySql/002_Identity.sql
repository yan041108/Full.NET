CREATE TABLE IF NOT EXISTS fn_identity_user
(
    Id char(36) NOT NULL PRIMARY KEY,
    TenantId char(36) NULL,
    ScopeKey varchar(64) NOT NULL,
    Username varchar(128) NOT NULL,
    NormalizedUsername varchar(128) NOT NULL,
    DisplayName varchar(128) NOT NULL,
    PasswordHash varchar(1024) NOT NULL,
    IsActive boolean NOT NULL,
    FailedLoginCount int NOT NULL DEFAULT 0,
    LockoutEndUtc datetime(6) NULL,
    SecurityStamp varchar(64) NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    UNIQUE KEY UX_fn_identity_user_Scope_Username (ScopeKey, NormalizedUsername)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_refresh_session
(
    Id char(36) NOT NULL PRIMARY KEY,
    UserId char(36) NOT NULL,
    FamilyId char(36) NOT NULL,
    ClientId varchar(64) NOT NULL,
    TokenHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ExpiresAtUtc datetime(6) NOT NULL,
    ConsumedAtUtc datetime(6) NULL,
    RevokedAtUtc datetime(6) NULL,
    ReplacedById char(36) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT FK_fn_identity_refresh_session_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id),
    UNIQUE KEY UX_fn_identity_refresh_session_TokenHash (TokenHash),
    KEY IX_fn_identity_refresh_session_Family (FamilyId, RevokedAtUtc, ExpiresAtUtc),
    KEY IX_fn_identity_refresh_session_User (UserId, RevokedAtUtc, ExpiresAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_identity_auth_audit
(
    Id char(36) NOT NULL PRIMARY KEY,
    UserId char(36) NULL,
    SessionId char(36) NULL,
    UsernameFingerprint char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    EventType varchar(64) NOT NULL,
    ResultCode varchar(128) NOT NULL,
    Succeeded boolean NOT NULL,
    IpAddress varchar(64) NULL,
    UserAgent varchar(512) NULL,
    OccurredAtUtc datetime(6) NOT NULL,
    CONSTRAINT FK_fn_identity_auth_audit_User
        FOREIGN KEY (UserId) REFERENCES fn_identity_user(Id),
    KEY IX_fn_identity_auth_audit_OccurredAt (OccurredAtUtc),
    KEY IX_fn_identity_auth_audit_User (UserId, OccurredAtUtc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
