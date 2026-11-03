-- 原子建表包含索引与约束，支持失败后重跑；没有指向其他模块的外键。
CREATE TABLE IF NOT EXISTS fn_organization_registration_assignment (
    Id BINARY(16) NOT NULL COMMENT '注册分配意图的应用端 UUID v7',
    TenantId BINARY(16) NOT NULL COMMENT '可信租户标识',
    MemberId BINARY(16) NOT NULL COMMENT '来源成员关系标识',
    MemberVersion int NOT NULL COMMENT '激活后的成员版本',
    UserId BINARY(16) NOT NULL COMMENT '目标账号标识',
    UnitId BINARY(16) NOT NULL COMMENT '目标机构单元标识',
    PositionId BINARY(16) NULL COMMENT '可选目标职位标识',
    Status varchar(16) NOT NULL COMMENT 'Pending、Completed 或 Skipped',
    ErrorCode varchar(160) NULL COMMENT '最近一次稳定错误码',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '激活事实时间',
    DueAtUtc datetime(6) NOT NULL COMMENT '下次处理时间',
    Version bigint NOT NULL COMMENT '本地并发版本',
    CONSTRAINT PK_fn_organization_registration_assignment PRIMARY KEY (Id),
    CONSTRAINT CK_fn_organization_registration_assignment_Status CHECK (Status IN ('Pending','Completed','Skipped')),
    CONSTRAINT CK_fn_organization_registration_assignment_Version CHECK (Version>0 AND MemberVersion>0),
    INDEX IX_fn_organization_registration_assignment_Due (Status,DueAtUtc,Id)
) COMMENT='注册方式组织分配的持久恢复意图' ENGINE=InnoDB;

