-- 134：平台更新日志主表与用户已读表。

CREATE TABLE IF NOT EXISTS fn_platform_release_note (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    VersionLabel varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '版本标签',
    VersionSortKey bigint NOT NULL COMMENT '版本排序键',
    Title varchar(200) NOT NULL COMMENT '标题',
    Content longtext NOT NULL COMMENT '正文内容',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    PublishedAtUtc datetime(6) NULL COMMENT '发布时间(UTC)',
    PublishedByUserId BINARY(16) NULL COMMENT '发布人用户标识',
    RetractedAtUtc datetime(6) NULL COMMENT '撤回时间(UTC)',
    RetractedByUserId BINARY(16) NULL COMMENT '撤回人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    UpdatedByUserId BINARY(16) NULL COMMENT '更新人用户标识',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_platform_release_note PRIMARY KEY (Id),
    CONSTRAINT UX_fn_platform_release_note_VersionLabel UNIQUE (VersionLabel),
    KEY IX_fn_platform_release_note_VersionSortKey (VersionSortKey DESC, Id)
) COMMENT='平台更新日志表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_platform_release_note_read (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ReleaseNoteId BINARY(16) NOT NULL COMMENT '更新日志标识',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    ReadAtUtc datetime(6) NOT NULL COMMENT '已读时间(UTC)',
    CONSTRAINT PK_fn_platform_release_note_read PRIMARY KEY (Id),
    CONSTRAINT UX_fn_platform_release_note_read_User_ReleaseNote UNIQUE (UserId, ReleaseNoteId),
    CONSTRAINT FK_fn_platform_release_note_read_ReleaseNote
        FOREIGN KEY (ReleaseNoteId) REFERENCES fn_platform_release_note (Id)
) COMMENT='平台更新日志用户已读表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_platform_release_note_indexes;
DELIMITER $$
CREATE PROCEDURE fn_platform_release_note_indexes()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_platform_release_note'
          AND INDEX_NAME = 'IX_fn_platform_release_note_VersionSortKey'
    )
    THEN
        CREATE INDEX IX_fn_platform_release_note_VersionSortKey
            ON fn_platform_release_note (VersionSortKey DESC, Id);
    END IF;
END$$
DELIMITER ;
CALL fn_platform_release_note_indexes();
DROP PROCEDURE fn_platform_release_note_indexes;
