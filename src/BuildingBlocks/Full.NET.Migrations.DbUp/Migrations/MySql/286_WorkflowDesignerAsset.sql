-- 286：完整 VForm 设计资产独立于业务校验投影；创建式 DDL 支持未记账后重跑。
CREATE TABLE IF NOT EXISTS fn_workflow_form_design_asset (
    FormId BINARY(16) NOT NULL COMMENT '表单定义标识',
    TenantScopeKey VARCHAR(64) NOT NULL COMMENT '可信作用域键',
    SourceCommit VARCHAR(40) NOT NULL COMMENT '固定上游源码提交',
    FormatVersion INT NOT NULL COMMENT '原版 JSON 格式版本',
    DesignerJson LONGTEXT NOT NULL COMMENT '完整原版设计 JSON',
    ContentHash VARCHAR(64) NOT NULL COMMENT '完整设计内容 SHA256',
    CONSTRAINT pk_fn_workflow_form_design_asset PRIMARY KEY (FormId),
    CONSTRAINT fk_fn_workflow_form_design_asset_owner FOREIGN KEY (FormId) REFERENCES fn_workflow_form_definition (Id) ON DELETE CASCADE
) COMMENT='工作流完整原版设计草稿资产' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_workflow_form_design_snapshot (
    FormVersionId BINARY(16) NOT NULL COMMENT '不可变表单版本标识',
    SourceCommit VARCHAR(40) NOT NULL COMMENT '固定上游源码提交',
    FormatVersion INT NOT NULL COMMENT '设计 JSON 格式版本',
    DesignerJson LONGTEXT NOT NULL COMMENT '完整不可变设计 JSON',
    DesignHash VARCHAR(64) NOT NULL COMMENT '设计内容摘要',
    RuntimeVersion VARCHAR(64) NOT NULL COMMENT '实际渲染产物 SHA256',
    BindingJson LONGTEXT NOT NULL COMMENT '发布时固化的组件绑定及运行目标',
    CONSTRAINT pk_fn_workflow_form_design_snapshot PRIMARY KEY (FormVersionId),
    CONSTRAINT fk_fn_workflow_form_design_snapshot_owner FOREIGN KEY (FormVersionId) REFERENCES fn_workflow_form_version (Id) ON DELETE CASCADE
) COMMENT='工作流不可变原版设计发布快照' ENGINE=InnoDB;
