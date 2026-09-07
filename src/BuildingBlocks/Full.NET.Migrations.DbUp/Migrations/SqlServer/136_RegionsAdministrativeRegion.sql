-- 136：行政区域主表与数据集清单表。

IF OBJECT_ID(N'dbo.fn_regions_administrative_region', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_regions_administrative_region
    (
        Id uniqueidentifier NOT NULL,
        ParentId uniqueidentifier NULL,
        Code varchar(12) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        ShortName nvarchar(64) NULL,
        MergerName nvarchar(256) NULL,
        ZipCode varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        CityCode varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        Level int NOT NULL,
        RegionType nvarchar(32) NULL,
        PinYin nvarchar(128) NULL,
        Longitude decimal(10, 6) NULL,
        Latitude decimal(10, 6) NULL,
        DisplayOrder int NOT NULL,
        Remark nvarchar(256) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_regions_administrative_region_Version DEFAULT (1),
        CONSTRAINT PK_fn_regions_administrative_region PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_regions_administrative_region_Code UNIQUE (Code),
        CONSTRAINT CK_fn_regions_administrative_region_Level CHECK (Level BETWEEN 1 AND 5),
        CONSTRAINT FK_fn_regions_administrative_region_Parent
            FOREIGN KEY (ParentId) REFERENCES dbo.fn_regions_administrative_region (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'行政区划行政区划表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'CityCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'城市编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'CityCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Code', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Code';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'DisplayOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Latitude', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'纬度', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Latitude';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Level', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'层级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Level';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Longitude', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'经度', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Longitude';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'MergerName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'合并名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'MergerName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'ParentId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'父级标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'ParentId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'PinYin', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'拼音', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'PinYin';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'RegionType', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'区划类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'RegionType';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Remark', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'备注', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Remark';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'ShortName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'简称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'ShortName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_administrative_region'), N'ZipCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'邮政编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region', @level2type=N'COLUMN', @level2name=N'ZipCode';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'行政区域表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_administrative_region';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_regions_administrative_region')
      AND indexObject.name = N'IX_fn_regions_administrative_region_ParentId'
)
    CREATE INDEX IX_fn_regions_administrative_region_ParentId
        ON dbo.fn_regions_administrative_region(ParentId, DisplayOrder, Code);

IF OBJECT_ID(N'dbo.fn_regions_dataset_manifest', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_regions_dataset_manifest
    (
        Id uniqueidentifier NOT NULL,
        DatasetKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DatasetVersion varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SourceDigest varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RecordCount int NOT NULL,
        AppliedAtUtc datetimeoffset(7) NOT NULL,
        AppliedByUserId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_regions_dataset_manifest PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_regions_dataset_manifest_Key_Version UNIQUE (DatasetKey, DatasetVersion)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'行政区划数据集清单表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_dataset_manifest'), N'AppliedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Applied At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest', @level2type=N'COLUMN', @level2name=N'AppliedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_dataset_manifest'), N'AppliedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用操作人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest', @level2type=N'COLUMN', @level2name=N'AppliedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_dataset_manifest'), N'DatasetKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据集键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest', @level2type=N'COLUMN', @level2name=N'DatasetKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_dataset_manifest'), N'DatasetVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'数据集版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest', @level2type=N'COLUMN', @level2name=N'DatasetVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_dataset_manifest'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_dataset_manifest'), N'RecordCount', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'记录数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest', @level2type=N'COLUMN', @level2name=N'RecordCount';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_regions_dataset_manifest'), N'SourceDigest', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'源摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest', @level2type=N'COLUMN', @level2name=N'SourceDigest';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_regions_dataset_manifest')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'行政区域数据集应用清单表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest';
END;
