-- 建立企业申请示例主表及明细表；MySQL 使用 BINARY(16) 与表列 COMMENT。
CREATE TABLE IF NOT EXISTS demo_enterprise_request_enterprise_request (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '所属租户标识',
    OrganizationUnitId BINARY(16) NOT NULL COMMENT '机构单元标识',
    RequestNumber varchar(64) NOT NULL COMMENT '申请单号',
    Title varchar(200) NOT NULL COMMENT '标题',
    Status varchar(32) NOT NULL COMMENT '状态',
    TotalAmount decimal(18, 2) NOT NULL COMMENT '申请总金额',
    ApplicantUserId BINARY(16) NOT NULL COMMENT '申请人用户标识',
    Version bigint NOT NULL COMMENT '乐观并发版本号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    UpdatedById BINARY(16) NULL COMMENT '更新人标识',
    IsDeleted boolean NOT NULL COMMENT '是否已软删除',
    DeletedAtUtc datetime(6) NULL COMMENT '删除时间(UTC)',
    DeletedById BINARY(16) NULL COMMENT '删除人标识',
    CONSTRAINT PK_demo_enterprise_request_enterprise_request PRIMARY KEY (Id),
    KEY IX_demo_enterprise_request_enterprise_request_TenantId_Id (TenantId, Id)
) COMMENT='企业申请示例主表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS demo_enterprise_request_enterprise_request_line (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '所属租户标识',
    RequestId BINARY(16) NOT NULL COMMENT '所属申请标识',
    LineNumber int NOT NULL COMMENT '明细行号',
    ItemDescription varchar(200) NOT NULL COMMENT '申请项目说明',
    Quantity decimal(18, 4) NOT NULL COMMENT '数量',
    UnitPrice decimal(18, 2) NOT NULL COMMENT '单价',
    LineAmount decimal(18, 2) NOT NULL COMMENT '明细金额',
    Version bigint NOT NULL COMMENT '乐观并发版本号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    UpdatedById BINARY(16) NULL COMMENT '更新人标识',
    IsDeleted boolean NOT NULL COMMENT '是否已软删除',
    DeletedAtUtc datetime(6) NULL COMMENT '删除时间(UTC)',
    DeletedById BINARY(16) NULL COMMENT '删除人标识',
    CONSTRAINT PK_demo_enterprise_request_enterprise_request_line PRIMARY KEY (Id),
    KEY IX_demo_enterprise_request_enterprise_request_line_Tena_d82d2acc (TenantId, RequestId)
) COMMENT='企业申请示例明细表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
