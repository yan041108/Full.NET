-- 095：在不修改已记账 094 的前提下，收敛事件流所有权约束并补种试点基线。

IF COL_LENGTH(N'dbo.fn_messaging_stream_ownership', N'RollbackState') IS NULL
BEGIN
    ALTER TABLE dbo.fn_messaging_stream_ownership
        ADD RollbackState tinyint NOT NULL
            CONSTRAINT DF_fn_messaging_stream_ownership_RollbackState DEFAULT (0);
END;
IF COL_LENGTH(N'dbo.fn_messaging_stream_ownership', N'RollbackGeneration') IS NULL
BEGIN
    ALTER TABLE dbo.fn_messaging_stream_ownership
        ADD RollbackGeneration uniqueidentifier NULL;
END;
IF COL_LENGTH(N'dbo.fn_messaging_stream_ownership', N'RollbackPreparedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_messaging_stream_ownership
        ADD RollbackPreparedAtUtc datetimeoffset(7) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_fn_messaging_stream_ownership_SchemaVersion')
BEGIN
    ALTER TABLE dbo.fn_messaging_stream_ownership
        ADD CONSTRAINT CK_fn_messaging_stream_ownership_SchemaVersion
            CHECK (SchemaVersion BETWEEN 1 AND 65535);
END;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_fn_messaging_stream_ownership_CurrentOwner')
BEGIN
    ALTER TABLE dbo.fn_messaging_stream_ownership
        ADD CONSTRAINT CK_fn_messaging_stream_ownership_CurrentOwner
            CHECK (CurrentOwner BETWEEN 0 AND 2);
END;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_fn_messaging_stream_ownership_PreviousOwner')
BEGIN
    ALTER TABLE dbo.fn_messaging_stream_ownership
        ADD CONSTRAINT CK_fn_messaging_stream_ownership_PreviousOwner
            CHECK (PreviousOwner BETWEEN 0 AND 2);
END;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_fn_messaging_stream_ownership_RollbackState')
BEGIN
    EXEC(N'
        ALTER TABLE dbo.fn_messaging_stream_ownership
            ADD CONSTRAINT CK_fn_messaging_stream_ownership_RollbackState
                CHECK (RollbackState BETWEEN 0 AND 1);');
END;

IF NOT EXISTS
(
    SELECT 1
    FROM dbo.fn_messaging_stream_ownership
    WHERE MessageType = 'fullnet.organization.unit.changed'
      AND SchemaVersion = 1
)
BEGIN
    EXEC(N'
        INSERT INTO dbo.fn_messaging_stream_ownership
        (
            MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
            CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
            Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
            RollbackState, RollbackGeneration, RollbackPreparedAtUtc,
            CreatedAtUtc, UpdatedAtUtc
        )
        VALUES
        (
            ''fullnet.organization.unit.changed'', 1, ''organization.unit-changed.v1'', 0, 0,
            ''01989abc-def0-7000-8000-000000000001'', SYSUTCDATETIME(), NULL, NULL,
            N''Initial legacy ownership baseline'', NULL, NULL,
            0, NULL, NULL,
            SYSUTCDATETIME(), SYSUTCDATETIME()
        );');
END;
