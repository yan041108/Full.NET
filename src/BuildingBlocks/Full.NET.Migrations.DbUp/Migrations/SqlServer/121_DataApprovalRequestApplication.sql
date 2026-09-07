-- 121：DataApproval 请求批准后业务应用结果与恢复元数据。
IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'ApplicationStatusKey') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD ApplicationStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_dataapproval_request_ApplicationStatusKey DEFAULT ('none');

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'ApplicationStatusKey', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'ApplicationStatusKey';

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'LastApplicationFailureCode') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD LastApplicationFailureCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'LastApplicationFailureCode', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次应用失败码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'LastApplicationFailureCode';

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'LastApplicationFailureMessage') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD LastApplicationFailureMessage nvarchar(512) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'LastApplicationFailureMessage', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近一次应用失败消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'LastApplicationFailureMessage';

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'LastApplicationAttemptAtUtc') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD LastApplicationAttemptAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'LastApplicationAttemptAtUtc', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Application Attempt At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'LastApplicationAttemptAtUtc';

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'ApplicationAttemptCount') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD ApplicationAttemptCount int NOT NULL
            CONSTRAINT DF_fn_dataapproval_request_ApplicationAttemptCount DEFAULT (0);

    IF NOT EXISTS (

        SELECT 1

        FROM sys.extended_properties

        WHERE class = 1

          AND major_id = OBJECT_ID(N'dbo.fn_dataapproval_request')

          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_dataapproval_request'), N'ApplicationAttemptCount', 'ColumnId')

          AND name = N'MS_Description'

    )

        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_dataapproval_request', @level2type=N'COLUMN', @level2name=N'ApplicationAttemptCount';

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
      AND name = N'CK_fn_dataapproval_request_ApplicationStatusKey')
    EXEC sys.sp_executesql N'
ALTER TABLE dbo.fn_dataapproval_request
    ADD CONSTRAINT CK_fn_dataapproval_request_ApplicationStatusKey
    CHECK (ApplicationStatusKey IN (''none'', ''pending_apply'', ''applied'', ''failed_retryable'', ''failed_terminal''));
';

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
      AND name = N'IX_fn_dataapproval_request_PendingApplication')
    EXEC sys.sp_executesql N'
CREATE INDEX IX_fn_dataapproval_request_PendingApplication
    ON dbo.fn_dataapproval_request (StatusKey, ApplicationStatusKey, LastApplicationAttemptAtUtc, SubmittedAtUtc)
    WHERE StatusKey = ''in_review'' AND ApplicationStatusKey IN (''pending_apply'', ''failed_retryable'');
';
