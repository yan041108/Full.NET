-- 127：工作流表单提交附件投影表，供 Files claim 探测与运行时授权校验。

CREATE TABLE IF NOT EXISTS fn_workflow_form_submission_attachment (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    SubmissionId BINARY(16) NOT NULL COMMENT '表单提交标识',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    FieldKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '字段键',
    FileId BINARY(16) NOT NULL COMMENT '附件文件标识',
    TenantScopeKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域键',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_workflow_form_submission_attachment PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_form_submission_attachment_Submission
        FOREIGN KEY (SubmissionId) REFERENCES fn_workflow_form_submission(Id),
    CONSTRAINT FK_fn_workflow_form_submission_attachment_Instance
        FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT UX_fn_workflow_form_submission_attachment_SubmissionFieldFile
        UNIQUE (SubmissionId, FieldKey, FileId)
) COMMENT='工作流表单提交附件投影表' ENGINE=InnoDB;

CREATE INDEX IX_fn_workflow_form_submission_attachment_SubmissionId
    ON fn_workflow_form_submission_attachment (SubmissionId);

CREATE INDEX IX_fn_workflow_form_submission_attachment_FileId
    ON fn_workflow_form_submission_attachment (FileId);
