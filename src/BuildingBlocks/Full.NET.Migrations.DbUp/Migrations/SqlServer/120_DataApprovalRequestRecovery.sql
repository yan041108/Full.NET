-- 120：DataApproval 请求工作流关联恢复元数据。
IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'RecoveryStatusKey') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD RecoveryStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_dataapproval_request_RecoveryStatusKey DEFAULT ('none');

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'LastFailureCode') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD LastFailureCode varchar(128) COLLATE Latin1_General_100_BIN2 NULL;

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'LastFailureMessage') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD LastFailureMessage nvarchar(512) NULL;

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'LastRecoveryAttemptAtUtc') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD LastRecoveryAttemptAtUtc datetimeoffset(7) NULL;

IF COL_LENGTH(N'dbo.fn_dataapproval_request', N'RecoveryAttemptCount') IS NULL
    ALTER TABLE dbo.fn_dataapproval_request
        ADD RecoveryAttemptCount int NOT NULL
            CONSTRAINT DF_fn_dataapproval_request_RecoveryAttemptCount DEFAULT (0);

EXEC sys.sp_executesql N'
UPDATE dbo.fn_dataapproval_request
SET RecoveryStatusKey = ''pending_link''
WHERE StatusKey = ''pending''
  AND WorkflowInstanceId IS NULL
  AND RecoveryStatusKey = ''none'';
';

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
      AND name = N'CK_fn_dataapproval_request_RecoveryStatusKey')
    EXEC sys.sp_executesql N'
ALTER TABLE dbo.fn_dataapproval_request
    ADD CONSTRAINT CK_fn_dataapproval_request_RecoveryStatusKey
    CHECK (RecoveryStatusKey IN (''none'', ''pending_link'', ''failed_retryable'', ''failed_terminal''));
';

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_dataapproval_request')
      AND name = N'IX_fn_dataapproval_request_PendingRecovery')
    EXEC sys.sp_executesql N'
CREATE INDEX IX_fn_dataapproval_request_PendingRecovery
    ON dbo.fn_dataapproval_request (StatusKey, RecoveryStatusKey, LastRecoveryAttemptAtUtc, SubmittedAtUtc)
    WHERE StatusKey = ''pending'' AND WorkflowInstanceId IS NULL;
';
