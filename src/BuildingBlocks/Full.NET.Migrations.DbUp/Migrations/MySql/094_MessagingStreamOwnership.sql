-- 094：事件流交付所有权持久化记录（试点切流/回退边界）。

CREATE TABLE IF NOT EXISTS fn_messaging_stream_ownership
(
    MessageType varchar(128) NOT NULL,
    SchemaVersion int NOT NULL,
    TopicCode varchar(128) NOT NULL,
    CurrentOwner tinyint NOT NULL,
    PreviousOwner tinyint NOT NULL,
    CutoffEventId binary(16) NOT NULL,
    CutoffOccurredAtUtc datetime(6) NOT NULL,
    CdcSourcePositionJson longtext NULL,
    OperatorUserId binary(16) NULL,
    Reason varchar(512) NOT NULL,
    RollbackBoundaryEventId binary(16) NULL,
    RollbackOccurredAtUtc datetime(6) NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NOT NULL,
    CONSTRAINT PK_fn_messaging_stream_ownership PRIMARY KEY (MessageType, SchemaVersion),
    CONSTRAINT CK_fn_messaging_stream_ownership_SchemaVersion CHECK (SchemaVersion BETWEEN 1 AND 65535),
    CONSTRAINT CK_fn_messaging_stream_ownership_CurrentOwner CHECK (CurrentOwner BETWEEN 0 AND 2),
    CONSTRAINT CK_fn_messaging_stream_ownership_PreviousOwner CHECK (PreviousOwner BETWEEN 0 AND 2)
) ENGINE=InnoDB;
