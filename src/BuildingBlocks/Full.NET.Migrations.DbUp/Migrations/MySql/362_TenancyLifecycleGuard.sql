-- 原子建表后独立补齐固定行，未记账失败或重复部署不重置既有修订。
CREATE TABLE IF NOT EXISTS fn_tenancy_lifecycle_guard (
    Id BINARY(16) NOT NULL COMMENT '固定的基础设施 UUID v7 标识',
    Version BIGINT NOT NULL COMMENT '领取事务锁时递增的修订',
    CONSTRAINT PK_fn_tenancy_lifecycle_guard PRIMARY KEY (Id)
) COMMENT='租户停止操作的全局事务串行门闩' ENGINE=InnoDB;
INSERT INTO fn_tenancy_lifecycle_guard(Id,Version)
SELECT UNHEX('019a490651a070008000000000000001'),1
WHERE NOT EXISTS (SELECT 1 FROM fn_tenancy_lifecycle_guard WHERE Id=UNHEX('019a490651a070008000000000000001'));
