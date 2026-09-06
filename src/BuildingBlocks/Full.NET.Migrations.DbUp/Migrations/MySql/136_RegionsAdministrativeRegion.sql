-- 136：行政区域主表与数据集清单表。

CREATE TABLE IF NOT EXISTS fn_regions_administrative_region (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ParentId BINARY(16) NULL COMMENT '父级区域标识',
    Code varchar(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '稳定区域编码',
    Name varchar(128) NOT NULL COMMENT '名称',
    ShortName varchar(64) NULL COMMENT '简称',
    MergerName varchar(256) NULL COMMENT '合并全称路径',
    ZipCode varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '邮政编码',
    CityCode varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '城市编码',
    Level int NOT NULL COMMENT '层级(1-5)',
    RegionType varchar(32) NULL COMMENT '区域类型',
    PinYin varchar(128) NULL COMMENT '拼音',
    Longitude decimal(10, 6) NULL COMMENT '经度',
    Latitude decimal(10, 6) NULL COMMENT '纬度',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    Remark varchar(256) NULL COMMENT '备注',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_regions_administrative_region PRIMARY KEY (Id),
    CONSTRAINT UX_fn_regions_administrative_region_Code UNIQUE (Code),
    CONSTRAINT CK_fn_regions_administrative_region_Level CHECK (Level BETWEEN 1 AND 5),
    CONSTRAINT FK_fn_regions_administrative_region_Parent
        FOREIGN KEY (ParentId) REFERENCES fn_regions_administrative_region (Id),
    KEY IX_fn_regions_administrative_region_ParentId (ParentId, DisplayOrder, Code)
) COMMENT='行政区域表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_regions_dataset_manifest (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    DatasetKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '数据集键',
    DatasetVersion varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '数据集版本',
    SourceDigest varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '来源摘要',
    RecordCount int NOT NULL COMMENT '记录数',
    AppliedAtUtc datetime(6) NOT NULL COMMENT '应用时间(UTC)',
    AppliedByUserId BINARY(16) NOT NULL COMMENT '应用人用户标识',
    CONSTRAINT PK_fn_regions_dataset_manifest PRIMARY KEY (Id),
    CONSTRAINT UX_fn_regions_dataset_manifest_Key_Version UNIQUE (DatasetKey, DatasetVersion)
) COMMENT='行政区域数据集应用清单表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

DROP PROCEDURE IF EXISTS fn_regions_administrative_region_indexes;
DELIMITER $$
CREATE PROCEDURE fn_regions_administrative_region_indexes()
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_regions_administrative_region'
          AND INDEX_NAME = 'IX_fn_regions_administrative_region_ParentId'
    )
    THEN
        CREATE INDEX IX_fn_regions_administrative_region_ParentId
            ON fn_regions_administrative_region (ParentId, DisplayOrder, Code);
    END IF;
END$$
DELIMITER ;
CALL fn_regions_administrative_region_indexes();
DROP PROCEDURE fn_regions_administrative_region_indexes;
