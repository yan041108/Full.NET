-- 249：受控补绑定与终态对账记录，只属于申请模块，不自动猜测或迁移历史业务状态。
IF OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_approval_repair (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        WorkflowInstanceId uniqueidentifier NOT NULL,
        RequestVersion bigint NOT NULL,
        KindKey varchar(32) NOT NULL,
        WorkflowStatusKey varchar(32) NOT NULL,
        ActorUserId uniqueidentifier NOT NULL,
        Reason nvarchar(500) NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_demo_enterprise_request_approval_repair PRIMARY KEY NONCLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请受控审批恢复记录', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复操作者标识，与原发起人区分', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复事务记录时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键及对账逻辑消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'KindKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'binding补绑定、start_receipt补启动回执或reconcile终态对账', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'KindKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'Reason', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者提供的恢复原因', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'Reason';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'RequestId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'申请标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'RequestId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'RequestVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复前申请版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'RequestVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'WorkflowInstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已核对原启动回执的流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'WorkflowInstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'WorkflowStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'读取时的权威流程状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'WorkflowStatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请受控审批恢复记录', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复操作者标识，与原发起人区分', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复事务记录时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键及对账逻辑消息标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'KindKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'binding补绑定或reconcile对账', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'KindKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'Reason', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者提供的恢复原因', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'Reason';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'RequestId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'申请标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'RequestId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'RequestVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'恢复前申请版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'RequestVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'WorkflowInstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已核对原启动回执的流程实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'WorkflowInstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair'), N'WorkflowStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'读取时的权威流程状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_approval_repair', @level2type=N'COLUMN', @level2name=N'WorkflowStatusKey';
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.demo_enterprise_request_approval_repair') AND name = N'UX_demo_enterprise_request_approval_repair_Request_Version_Kind')
    CREATE UNIQUE CLUSTERED INDEX UX_demo_enterprise_request_approval_repair_Request_Version_Kind
        ON dbo.demo_enterprise_request_approval_repair (TenantId, RequestId, RequestVersion, KindKey);
