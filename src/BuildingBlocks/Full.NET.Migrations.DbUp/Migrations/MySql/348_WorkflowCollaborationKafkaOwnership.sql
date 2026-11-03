-- 只登记协作流的生产安全Legacy基线；不切流、不填CDC位点，也不覆盖已有控制状态。
INSERT INTO fn_messaging_stream_ownership
    (MessageType, SchemaVersion, TopicCode, CurrentOwner, PreviousOwner,
     CutoffEventId, CutoffOccurredAtUtc, CdcSourcePositionJson, OperatorUserId,
     Reason, RollbackBoundaryEventId, RollbackOccurredAtUtc,
     RollbackState, RollbackGeneration, RollbackPreparedAtUtc, CreatedAtUtc, UpdatedAtUtc)
SELECT stream.MessageType, 1, stream.TopicCode, 0, 0,
       0x01989abcdef070008000000000000001, UTC_TIMESTAMP(6), NULL, NULL,
       'Workflow collaboration Legacy baseline', NULL, NULL, 0, NULL, NULL, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
FROM (
    SELECT 'fullnet.workflow.collaboration.requested' AS MessageType, 'workflow.collaboration.requested.v1' AS TopicCode
    UNION ALL SELECT 'fullnet.workflow.collaboration.created', 'workflow.collaboration.created.v1'
    UNION ALL SELECT 'fullnet.workflow.collaboration.completed', 'workflow.collaboration.completed.v1'
    UNION ALL SELECT 'fullnet.workflow.collaboration.cancelled', 'workflow.collaboration.cancelled.v1'
) AS stream
WHERE NOT EXISTS (
    SELECT 1 FROM fn_messaging_stream_ownership AS existing
    WHERE existing.MessageType = stream.MessageType AND existing.SchemaVersion = 1
);
