-- 280：Notifications 发送配额治理（Provider/租户/Producer/Scene）。
-- 政策表声明有界额度；计数器表按窗口原子预留，跨实例不超卖；预留台账支撑成功核销、
-- 确定未发送释放与 Unknown 暂挂对账。缺失配置时发送链路失败关闭，不允许无界并发。

CREATE TABLE IF NOT EXISTS fn_notifications_quota_policy (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    DimensionKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '配额维度键：provider/tenant/producer/scene',
    DimensionValue varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '配额维度取值；* 表示该维度全部取值',
    WindowSeconds int NOT NULL COMMENT '固定窗口长度（秒）；窗口起点按 Unix 时间对齐',
    MaxCount int NOT NULL COMMENT '单个窗口内允许的发送上限（不含已释放的确定未发送）',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '政策是否启用；停用的政策不再参与配额解析',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_notifications_quota_policy PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_quota_policy_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_notifications_quota_policy_Dimension CHECK (DimensionKey IN ('provider', 'tenant', 'producer', 'scene')),
    CONSTRAINT CK_fn_notifications_quota_policy_Window CHECK (WindowSeconds > 0),
    CONSTRAINT CK_fn_notifications_quota_policy_MaxCount CHECK (MaxCount > 0),
    CONSTRAINT CK_fn_notifications_quota_policy_Version CHECK (Version > 0),
    CONSTRAINT UX_fn_notifications_quota_policy_Scope UNIQUE (TenantScopeKey, DimensionKey, DimensionValue)
) COMMENT='通知发送配额政策表；子租户精确声明优先于 host 通配声明' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_quota_counter (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    PolicyId BINARY(16) NOT NULL COMMENT '配额政策标识',
    WindowStartUtc datetime(6) NOT NULL COMMENT '窗口起点(UTC)',
    WindowSeconds int NOT NULL COMMENT '窗口长度（秒）',
    ReservedCount int NOT NULL DEFAULT 0 COMMENT '累计预留次数；含已核销与暂挂对账',
    ConsumedCount int NOT NULL DEFAULT 0 COMMENT '成功核销次数',
    ReleasedCount int NOT NULL DEFAULT 0 COMMENT '确定未发送而释放的次数；释放后额度回到窗口',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_notifications_quota_counter PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_quota_counter_Policy FOREIGN KEY (PolicyId) REFERENCES fn_notifications_quota_policy(Id),
    CONSTRAINT UX_fn_notifications_quota_counter_Window UNIQUE (PolicyId, WindowStartUtc),
    CONSTRAINT CK_fn_notifications_quota_counter_Counts CHECK (ReservedCount >= 0 AND ConsumedCount >= 0 AND ReleasedCount >= 0)
) COMMENT='通知发送配额窗口计数器；在飞占用=ReservedCount-ReleasedCount' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_quota_reservation (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    DeliveryId BINARY(16) NOT NULL COMMENT '投递标识',
    PolicyId BINARY(16) NOT NULL COMMENT '配额政策标识',
    DimensionKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '配额维度键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    WindowStartUtc datetime(6) NOT NULL COMMENT '预留所属窗口起点(UTC)',
    LeaseGeneration bigint NOT NULL COMMENT '预留所属领取世代；同一次领取只占额一次',
    StateKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '预留状态：reserved/consumed/released/pending_reconciliation',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_notifications_quota_reservation PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_quota_reservation_Delivery FOREIGN KEY (DeliveryId) REFERENCES fn_notifications_delivery(Id),
    CONSTRAINT FK_fn_notifications_quota_reservation_Policy FOREIGN KEY (PolicyId) REFERENCES fn_notifications_quota_policy(Id),
    CONSTRAINT CK_fn_notifications_quota_reservation_State CHECK (StateKey IN ('reserved', 'consumed', 'released', 'pending_reconciliation')),
    CONSTRAINT CK_fn_notifications_quota_reservation_LeaseGen CHECK (LeaseGeneration > 0),
    CONSTRAINT UX_fn_notifications_quota_reservation_Attempt UNIQUE (DeliveryId, PolicyId, LeaseGeneration)
) COMMENT='通知发送配额预留台账；一次领取一批预留，按结果核销、释放或暂挂对账' ENGINE=InnoDB;
