-- 建立企业申请示例主表及明细表；SQL Server 使用 uniqueidentifier、显式非聚集主键与扩展属性注释。
IF OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_enterprise_request
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OrganizationUnitId uniqueidentifier NOT NULL,
        RequestNumber nvarchar(64) NOT NULL,
        Title nvarchar(200) NOT NULL,
        Status nvarchar(32) NOT NULL,
        TotalAmount decimal(18, 2) NOT NULL,
        ApplicantUserId uniqueidentifier NOT NULL,
        Version bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedById uniqueidentifier NULL,
        IsDeleted bit NOT NULL,
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedById uniqueidentifier NULL,
        CONSTRAINT PK_demo_enterprise_request_enterprise_request PRIMARY KEY NONCLUSTERED (Id)
    );

    CREATE CLUSTERED INDEX IX_demo_enterprise_request_enterprise_request_TenantId_Id
        ON dbo.demo_enterprise_request_enterprise_request(TenantId, Id);
END;

-- 注释独立于建表分支，支持表已创建但迁移尚未记账时补齐。
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请示例主表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'ApplicantUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'申请人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'ApplicantUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'DeletedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'DeletedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'IsDeleted', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已软删除', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'OrganizationUnitId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'机构单元标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'OrganizationUnitId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'RequestNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'申请单号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'RequestNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'Title', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'Title';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'TotalAmount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'申请总金额', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'TotalAmount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'UpdatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'UpdatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request', @level2type=N'COLUMN', @level2name=N'Version';

IF OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.demo_enterprise_request_enterprise_request_line
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        RequestId uniqueidentifier NOT NULL,
        LineNumber int NOT NULL,
        ItemDescription nvarchar(200) NOT NULL,
        Quantity decimal(18, 4) NOT NULL,
        UnitPrice decimal(18, 2) NOT NULL,
        LineAmount decimal(18, 2) NOT NULL,
        Version bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedById uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedById uniqueidentifier NULL,
        IsDeleted bit NOT NULL,
        DeletedAtUtc datetimeoffset(7) NULL,
        DeletedById uniqueidentifier NULL,
        CONSTRAINT PK_demo_enterprise_request_enterprise_request_line PRIMARY KEY NONCLUSTERED (Id)
    );

    CREATE CLUSTERED INDEX IX_demo_enterprise_request_enterprise_request_line_Tena_d82d2acc
        ON dbo.demo_enterprise_request_enterprise_request_line(TenantId, RequestId);
END;

-- 注释独立于建表分支，支持表已创建但迁移尚未记账时补齐。
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'企业申请示例明细表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'DeletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'DeletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'DeletedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'删除人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'DeletedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'IsDeleted', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否已软删除', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'IsDeleted';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'ItemDescription', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'申请项目说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'ItemDescription';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'LineAmount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'明细金额', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'LineAmount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'LineNumber', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'明细行号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'LineNumber';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'Quantity', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数量', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'Quantity';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'RequestId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属申请标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'RequestId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属租户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'UnitPrice', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'单价', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'UnitPrice';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'UpdatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'UpdatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request_line'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'demo_enterprise_request_enterprise_request_line', @level2type=N'COLUMN', @level2name=N'Version';
