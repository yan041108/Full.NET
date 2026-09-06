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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'行政区域数据集应用清单表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_regions_dataset_manifest';
END;
