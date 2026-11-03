-- 只登记协作流的生产安全Legacy基线；不切流、不填CDC位点，也不覆盖已有控制状态。
INSERT INTO dbo.fn_messaging_stream_ownership
    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
     CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
     Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
     RollbackState, RollbackGeneration, RollbackPreparedAtUtc, CreatedAtUtc, UpdatedAtUtc)
SELECT stream.MessageType, 1, stream.TopicCode, 0, 0,
       CONVERT(uniqueidentifier, '01989abc-def0-7000-8000-000000000001'), SYSUTCDATETIME(), NULL, NULL,
       N'Workflow collaboration Legacy baseline', NULL, NULL, 0, NULL, NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
FROM (VALUES
    ('fullnet.workflow.collaboration.requested', 'workflow.collaboration.requested.v1'),
    ('fullnet.workflow.collaboration.created', 'workflow.collaboration.created.v1'),
    ('fullnet.workflow.collaboration.completed', 'workflow.collaboration.completed.v1'),
    ('fullnet.workflow.collaboration.cancelled', 'workflow.collaboration.cancelled.v1')
) AS stream(MessageType, TopicCode)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.fn_messaging_stream_ownership AS existing WITH (UPDLOCK, HOLDLOCK)
    WHERE existing.MessageType = stream.MessageType AND existing.SchemaVersion = 1
);
