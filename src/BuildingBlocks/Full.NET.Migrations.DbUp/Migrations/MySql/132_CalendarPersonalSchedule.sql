-- 132：用户个人日程表。

CREATE TABLE IF NOT EXISTS fn_calendar_personal_schedule (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    OwnerUserId BINARY(16) NOT NULL COMMENT '所属用户标识',
    Content varchar(256) NOT NULL COMMENT '日程内容',
    StartAtUtc datetime(6) NOT NULL COMMENT '开始时间(UTC)',
    EndAtUtc datetime(6) NOT NULL COMMENT '结束时间(UTC)',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_calendar_personal_schedule PRIMARY KEY (Id),
    CONSTRAINT CK_fn_calendar_personal_schedule_TimeRange CHECK (EndAtUtc >= StartAtUtc),
    KEY IX_fn_calendar_personal_schedule_OwnerStartAtUtc (OwnerUserId, StartAtUtc, Id)
) COMMENT='用户个人日程表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_calendar_personal_schedule_indexes;
DELIMITER $$
CREATE PROCEDURE fn_calendar_personal_schedule_indexes()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_calendar_personal_schedule'
          AND INDEX_NAME = 'IX_fn_calendar_personal_schedule_OwnerStartAtUtc'
    )
    THEN
        CREATE INDEX IX_fn_calendar_personal_schedule_OwnerStartAtUtc
            ON fn_calendar_personal_schedule (OwnerUserId, StartAtUtc, Id);
    END IF;
END$$
DELIMITER ;
CALL fn_calendar_personal_schedule_indexes();
DROP PROCEDURE fn_calendar_personal_schedule_indexes;
