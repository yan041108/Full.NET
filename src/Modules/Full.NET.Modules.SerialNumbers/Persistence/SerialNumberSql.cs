using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.SerialNumbers.Persistence;

/// <summary>
/// Host/Tenant 流水号规则与计数器分配的 Dapper SQL 语句集。
/// 关键并发正确性边界：Allocate* 系列语句必须在数据库事务内通过命名锁
/// （SQL Server 用 sp_getapplock，MySQL 用 GET_LOCK 或 ON DUPLICATE KEY UPDATE 的原子语义）
/// 串行化同一 (RuleId, TenantId, ResetBucket) 计数器的推进，
/// 禁止在应用层用内存锁承担正确性。规则查询使用 SqlDataScope.HostOnly，
/// 而分配与计数器查询使用 Global/TenantRequired 以跨 Host 行或绑定当前 Tenant。
/// </summary>
internal static class SerialNumberSql
{
    /// <summary>
    /// 列表筛选条件：名称/键模糊匹配与启停状态；空参数表示不过滤。
    /// </summary>
    private const string RuleListWhereSqlServer = """
        (@NameContains IS NULL OR DisplayName LIKE '%' + @NameContains + '%')
          AND (@KeyContains IS NULL OR RuleKey LIKE '%' + @KeyContains + '%')
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """;

    private const string RuleListWhereMySql = """
        (@NameContains IS NULL OR DisplayName LIKE CONCAT('%', @NameContains, '%'))
          AND (@KeyContains IS NULL OR RuleKey LIKE CONCAT('%', @KeyContains, '%'))
          AND (@IsEnabled IS NULL OR IsEnabled = @IsEnabled)
        """;

    /// <summary>规则列表投影字段；与 SerialNumberRuleRecord 属性顺序对齐以支持 Dapper 直接映射。</summary>
    private const string RuleListProjection = """
        Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
        Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
        CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
        Version
        """;

    /// <summary>SQL Server 规则分页模板；排序占位符只允许由白名单解析器替换。</summary>
    private static readonly SqlStatement PageRulesSqlServerTemplate = new(
        "serial_numbers.rule.page.sql_server",
        $"""
        SELECT COUNT(*) FROM fn_serialnumbers_rule
        WHERE {RuleListWhereSqlServer};
        SELECT {RuleListProjection}
        FROM fn_serialnumbers_rule
        WHERE {RuleListWhereSqlServer}
        ORDER BY __ORDER_BY__
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.HostOnly);

    /// <summary>MySQL 规则分页模板；排序占位符只允许由白名单解析器替换。</summary>
    private static readonly SqlStatement PageRulesMySqlTemplate = new(
        "serial_numbers.rule.page.my_sql",
        $"""
        SELECT COUNT(*) FROM fn_serialnumbers_rule
        WHERE {RuleListWhereMySql};
        SELECT {RuleListProjection}
        FROM fn_serialnumbers_rule
        WHERE {RuleListWhereMySql}
        ORDER BY __ORDER_BY__
        LIMIT @PageSize OFFSET @Offset;
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按白名单排序列与方向组装分页 SQL；末尾固定 Id 保证跨页顺序稳定。
    /// </summary>
    /// <param name="orderByClause">经 <see cref="ResolveRuleListOrderBy"/> 生成的白名单排序片段。</param>
    /// <returns>带确定排序的 SQL Server 分页语句。</returns>
    public static SqlStatement CreatePageRulesSqlServer(string orderByClause) =>
        PageRulesSqlServerTemplate with
        {
            Text = PageRulesSqlServerTemplate.Text.Replace(
                "__ORDER_BY__",
                orderByClause,
                StringComparison.Ordinal),
        };

    /// <summary>
    /// MySQL 等价分页语句：使用 LIMIT/OFFSET 语法，语义与 SQL Server 版本一致。
    /// </summary>
    /// <param name="orderByClause">经 <see cref="ResolveRuleListOrderBy"/> 生成的白名单排序片段。</param>
    /// <returns>带确定排序的 MySQL 分页语句。</returns>
    public static SqlStatement CreatePageRulesMySql(string orderByClause) =>
        PageRulesMySqlTemplate with
        {
            Text = PageRulesMySqlTemplate.Text.Replace(
                "__ORDER_BY__",
                orderByClause,
                StringComparison.Ordinal),
        };

    /// <summary>
    /// 将 sortBy/sortDirection 解析为仅含白名单列的 ORDER BY 片段。
    /// </summary>
    public static string ResolveRuleListOrderBy(
        string? sortBy,
        string? sortDirection)
    {
        var ascending = !string.Equals(
            sortDirection?.Trim(),
            "desc",
            StringComparison.OrdinalIgnoreCase);
        var direction = ascending ? "ASC" : "DESC";
        var column = (sortBy?.Trim() ?? string.Empty).ToLowerInvariant() switch
        {
            "rulekey" or "key" => "RuleKey",
            "displayname" or "name" => "DisplayName",
            "createdatutc" => "CreatedAtUtc",
            "isenabled" or "status" => "IsEnabled",
            _ => "DisplayOrder",
        };
        return $"{column} {direction}, Id ASC";
    }

    /// <summary>
    /// 按 Id 查询规则；Host 行读，仅用于管理端单条详情，不持有任何锁。
    /// </summary>
    public static readonly SqlStatement FindRuleById = new(
        "serial_numbers.rule.find_by_id",
        """
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule
        WHERE Id = @Id
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按 RuleKey 查询规则；使用 Global 作用域以允许 Host 与 Tenant 上下文都能查找规则定义，
    /// 用于分配前的规则校验，是 Host 与 Tenant 共享规则目录的读取边界。
    /// </summary>
    public static readonly SqlStatement FindRuleByKey = new(
        "serial_numbers.rule.find_by_key",
        """
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule
        WHERE RuleKey = @RuleKey
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 分配前按 RuleKey 加共享锁读取规则（SQL Server）：HOLDLOCK 串行化 RuleKey 读取，
    /// 防止分配期间规则被并发修改导致 Pattern/Scope 等关键字段瞬时漂移；
    /// 与 Allocate* 语句的事务边界协同，是并发生成的入口锁。
    /// </summary>
    public static readonly SqlStatement LockRuleForAllocationSqlServer = new(
        "serial_numbers.rule.lock_for_allocation.sql_server",
        """
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule WITH (HOLDLOCK)
        WHERE RuleKey = @RuleKey
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 分配前按 RuleKey 加共享锁读取规则（MySQL）：FOR SHARE 等价于 SQL Server 的 HOLDLOCK，
    /// 与 AllocateTenantMySql 的 ON DUPLICATE KEY UPDATE 配合保证计数器推进原子性。
    /// </summary>
    public static readonly SqlStatement LockRuleForAllocationMySql = new(
        "serial_numbers.rule.lock_for_allocation.my_sql",
        """
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule
        WHERE RuleKey = @RuleKey
        FOR SHARE
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 变更规则前按 Id 加更新锁（SQL Server）：UPDLOCK+HOLDLOCK 升级为排他锁，
    /// 与 UpdateRule/SetRuleEnabled 同事务串行化规则变更，防止 RuleKey 重复或 Pattern 瞬时漂移。
    /// </summary>
    public static readonly SqlStatement LockRuleForMutationSqlServer = new(
        "serial_numbers.rule.lock_for_mutation.sql_server",
        """
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule WITH (UPDLOCK, HOLDLOCK)
        WHERE Id = @Id
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 变更规则前按 Id 加更新锁（MySQL）：FOR UPDATE 等价于 SQL Server 的 UPDLOCK+HOLDLOCK。
    /// </summary>
    public static readonly SqlStatement LockRuleForMutationMySql = new(
        "serial_numbers.rule.lock_for_mutation.my_sql",
        """
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule
        WHERE Id = @Id
        FOR UPDATE
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 统计规则已分配的流水号数量；用于删除规则前的引用完整性校验，存在分配记录时禁止删除。
    /// </summary>
    public static readonly SqlStatement CountAllocationsByRule = new(
        "serial_numbers.allocation.count_by_rule",
        """
        SELECT COUNT(*)
        FROM fn_serialnumbers_allocation
        WHERE RuleId = @RuleId
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 插入规则：RuleKey 唯一约束在数据库层保证；Version 初始化为 1，CreatedAtUtc 显式注入，
    /// 禁止依赖数据库默认值以保持 SQL Server/MySQL 行为一致。
    /// </summary>
    public static readonly SqlStatement InsertRule = new(
        "serial_numbers.rule.insert",
        """
        INSERT INTO fn_serialnumbers_rule
            (Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
             Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
             CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
             Version)
        VALUES
            (@Id, @RuleKey, @DisplayName, @Description, @Scope, @ResetInterval,
             @Pattern, @MinimumValue, @MaximumValue, @DisplayOrder, @IsEnabled,
             @CreatedAtUtc, @CreatedByUserId, NULL, NULL, 1)
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 更新规则：通过 Version = @Version 实现乐观并发控制，受影响行数为 0 表示版本冲突，
    /// 必须返回 409 Conflict；不得移除 Version 条件，否则会破坏跨客户端覆盖保护。
    /// </summary>
    public static readonly SqlStatement UpdateRule = new(
        "serial_numbers.rule.update",
        """
        UPDATE fn_serialnumbers_rule
        SET DisplayName = @DisplayName,
            Description = @Description,
            Scope = @Scope,
            ResetInterval = @ResetInterval,
            Pattern = @Pattern,
            MinimumValue = @MinimumValue,
            MaximumValue = @MaximumValue,
            DisplayOrder = @DisplayOrder,
            IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 启停规则：仅切换 IsEnabled，不改 Pattern/Scope 等关键字段；同样受 Version 乐观并发保护。
    /// </summary>
    public static readonly SqlStatement SetRuleEnabled = new(
        "serial_numbers.rule.set_enabled",
        """
        UPDATE fn_serialnumbers_rule
        SET IsEnabled = @IsEnabled,
            UpdatedAtUtc = @UpdatedAtUtc,
            UpdatedByUserId = @UpdatedByUserId,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    /// <summary>
    /// 按 IdempotencyKey 查询 Host 分配记录：用于幂等回放，调用方在 UniqueConstraint 异常后
    /// 必须重新查询此语句以返回已分配的 SerialNumber，避免重复扣减计数器。
    /// </summary>
    public static readonly SqlStatement FindHostAllocation = new(
        "serial_numbers.allocation.find_host_idempotency",
        """
        SELECT RuleKey, SerialNumber, SequenceValue, ResetBucket, AllocatedAtUtc
        FROM fn_serialnumbers_allocation
        WHERE RuleId = @RuleId
          AND TenantId IS NULL
          AND IdempotencyKey = @IdempotencyKey
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 按 IdempotencyKey 查询 Tenant 分配记录：与 Host 版本语义等价，按 TenantId 隔离幂等键空间。
    /// </summary>
    public static readonly SqlStatement FindTenantAllocation = new(
        "serial_numbers.allocation.find_tenant_idempotency",
        """
        SELECT RuleKey, SerialNumber, SequenceValue, ResetBucket, AllocatedAtUtc
        FROM fn_serialnumbers_allocation
        WHERE RuleId = @RuleId
          AND TenantId = @TenantId
          AND IdempotencyKey = @IdempotencyKey
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>
    /// Host 计数器原子分配（SQL Server）：sp_getapplock 命名锁串行化同一
    /// (RuleId, ResetBucket) 计数器推进；UPDATE...OUTPUT inserted.LastValue 在锁内原子自增并回读，
    /// 计数器不存在时回退 INSERT 初值；LastValue 达到 MaximumValue 时 OUTPUT 为空，
    /// 上层据此判定 SequenceExhausted。命名锁 Resource 由 SerialNumberAllocator.CreateLockResource 拼装。
    /// </summary>
    public static readonly SqlStatement AllocateHostSqlServer = new(
        "serial_numbers.counter.allocate_host.sql_server",
        """
        DECLARE @Allocated TABLE (Value bigint NOT NULL);
        DECLARE @LockResult int;

        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 30000;

        IF @LockResult < 0
            THROW 51000, 'Unable to acquire the serial number counter lock.', 1;

        UPDATE counter
        SET counter.LastValue = counter.LastValue + 1,
            counter.UpdatedAtUtc = @UpdatedAtUtc
        OUTPUT inserted.LastValue INTO @Allocated(Value)
        FROM fn_serialnumbers_counter AS counter
        WHERE counter.RuleId = @RuleId
          AND counter.TenantId IS NULL
          AND counter.ResetBucket = @ResetBucket
          AND counter.LastValue < @MaximumValue;

        IF NOT EXISTS (SELECT 1 FROM @Allocated)
           AND NOT EXISTS
           (
               SELECT 1
               FROM fn_serialnumbers_counter
               WHERE RuleId = @RuleId
                 AND TenantId IS NULL
                 AND ResetBucket = @ResetBucket
           )
        BEGIN
            INSERT INTO fn_serialnumbers_counter
                (Id, RuleId, TenantId, ResetBucket, LastValue, UpdatedAtUtc)
            VALUES
                (@CounterId, @RuleId, NULL, @ResetBucket, @MinimumValue,
                 @UpdatedAtUtc);
            INSERT INTO @Allocated(Value) VALUES (@MinimumValue);
        END;

        SELECT Value FROM @Allocated;
        """,
        SqlDataScope.Global);

    /// <summary>
    /// Tenant 计数器原子分配（SQL Server）：与 Host 版本语义等价，额外按 TenantId 过滤；
    /// TenantId 不可为 NULL，由 SqlTenantBinding.CurrentTenantId 在执行前绑定当前租户。
    /// </summary>
    public static readonly SqlStatement AllocateTenantSqlServer = new(
        "serial_numbers.counter.allocate_tenant.sql_server",
        """
        DECLARE @Allocated TABLE (Value bigint NOT NULL);
        DECLARE @LockResult int;

        EXEC @LockResult = sys.sp_getapplock
            @Resource = @LockResource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 30000;

        IF @LockResult < 0
            THROW 51000, 'Unable to acquire the serial number counter lock.', 1;

        UPDATE counter
        SET counter.LastValue = counter.LastValue + 1,
            counter.UpdatedAtUtc = @UpdatedAtUtc
        OUTPUT inserted.LastValue INTO @Allocated(Value)
        FROM fn_serialnumbers_counter AS counter
        WHERE counter.TenantId = @TenantId
          AND counter.TenantId IS NOT NULL
          AND counter.RuleId = @RuleId
          AND counter.ResetBucket = @ResetBucket
          AND counter.LastValue < @MaximumValue;

        IF NOT EXISTS (SELECT 1 FROM @Allocated)
           AND NOT EXISTS
           (
               SELECT 1
               FROM fn_serialnumbers_counter
               WHERE TenantId = @TenantId
                 AND TenantId IS NOT NULL
                 AND RuleId = @RuleId
                 AND ResetBucket = @ResetBucket
           )
        BEGIN
            INSERT INTO fn_serialnumbers_counter
                (Id, RuleId, TenantId, ResetBucket, LastValue, UpdatedAtUtc)
            VALUES
                (@CounterId, @RuleId, @TenantId, @ResetBucket, @MinimumValue,
                 @UpdatedAtUtc);
            INSERT INTO @Allocated(Value) VALUES (@MinimumValue);
        END;

        SELECT Value FROM @Allocated;
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>
    /// Host 计数器原子分配（MySQL）：通过 INSERT...ON DUPLICATE KEY UPDATE + LAST_INSERT_ID 实现
    /// 原子自增与回读，等价于 SQL Server 的 sp_getapplock+UPDATE OUTPUT；LAST_INSERT_ID() > 0
    /// 表示成功自增，返回 0 表示达到 MaximumValue，上层据此判定 SequenceExhausted。
    /// 注意：MySQL 不依赖应用层锁，原子性由唯一键 (RuleId, TenantId, ResetBucket) 与
    /// LAST_INSERT_ID 在同会话内的可见性保证。
    /// </summary>
    public static readonly SqlStatement AllocateHostMySql = new(
        "serial_numbers.counter.allocate_host.my_sql",
        """
        DO LAST_INSERT_ID(0);
        INSERT INTO fn_serialnumbers_counter
            (Id, RuleId, TenantId, ResetBucket, LastValue, UpdatedAtUtc)
        VALUES
            (@CounterId, @RuleId, NULL, @ResetBucket,
             LAST_INSERT_ID(@MinimumValue), @UpdatedAtUtc)
        ON DUPLICATE KEY UPDATE
            LastValue = IF(
                LastValue < @MaximumValue,
                LAST_INSERT_ID(LastValue + 1),
                LastValue + LAST_INSERT_ID(0)),
            UpdatedAtUtc = @UpdatedAtUtc;
        SELECT LAST_INSERT_ID() AS Value
        WHERE LAST_INSERT_ID() > 0;
        """,
        SqlDataScope.Global);

    /// <summary>
    /// Tenant 计数器原子分配（MySQL）：与 Host 版本语义等价，按 TenantId 区分计数器行；
    /// TenantId 由 SqlTenantBinding.CurrentTenantId 绑定，不可为 NULL。
    /// </summary>
    public static readonly SqlStatement AllocateTenantMySql = new(
        "serial_numbers.counter.allocate_tenant.my_sql",
        """
        DO LAST_INSERT_ID(0);
        INSERT INTO fn_serialnumbers_counter
            (Id, RuleId, TenantId, ResetBucket, LastValue, UpdatedAtUtc)
        VALUES
            (@CounterId, @RuleId, @TenantId, @ResetBucket,
             LAST_INSERT_ID(@MinimumValue), @UpdatedAtUtc)
        ON DUPLICATE KEY UPDATE
            LastValue = IF(
                LastValue < @MaximumValue,
                LAST_INSERT_ID(LastValue + 1),
                LastValue + LAST_INSERT_ID(0)),
            UpdatedAtUtc = @UpdatedAtUtc;
        SELECT LAST_INSERT_ID() AS Value
        WHERE LAST_INSERT_ID() > 0;
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);

    /// <summary>
    /// 持久化 Host 分配记录：写入 (RuleId, NULL, IdempotencyKey, SequenceValue, SerialNumber)；
    /// IdempotencyKey + RuleId + TenantId 联合唯一键保证同一幂等键重复请求会触发 UniqueConstraint，
    /// 由 SerialNumberAllocator 捕获后查询既有分配回放，是幂等性的物理边界。
    /// </summary>
    public static readonly SqlStatement InsertHostAllocation = new(
        "serial_numbers.allocation.insert_host",
        """
        INSERT INTO fn_serialnumbers_allocation
            (Id, RuleId, TenantId, RuleKey, ResetBucket, IdempotencyKey,
             SequenceValue, SerialNumber, AllocatedAtUtc)
        VALUES
            (@Id, @RuleId, NULL, @RuleKey, @ResetBucket, @IdempotencyKey,
             @SequenceValue, @SerialNumber, @AllocatedAtUtc)
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 持久化 Tenant 分配记录：与 Host 版本语义等价，按 TenantId 隔离分配历史。
    /// </summary>
    public static readonly SqlStatement InsertTenantAllocation = new(
        "serial_numbers.allocation.insert_tenant",
        """
        INSERT INTO fn_serialnumbers_allocation
            (Id, RuleId, TenantId, RuleKey, ResetBucket, IdempotencyKey,
             SequenceValue, SerialNumber, AllocatedAtUtc)
        VALUES
            (@Id, @RuleId, @TenantId, @RuleKey, @ResetBucket, @IdempotencyKey,
             @SequenceValue, @SerialNumber, @AllocatedAtUtc)
        """,
        SqlDataScope.TenantRequired,
        SqlTenantBinding.CurrentTenantId);
}

/// <summary>
/// 流水号规则持久化记录；与 RuleListProjection 等查询投影字段顺序对齐以支持 Dapper 直接映射，
/// Scope/ResetInterval 以 int 存储并在应用层枚举转换，避免跨库枚举序列化差异。
/// </summary>
internal sealed class SerialNumberRuleRecord
{
    public Guid Id { get; set; }

    public string RuleKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Scope { get; set; }

    public int ResetInterval { get; set; }

    public string Pattern { get; set; } = string.Empty;

    public long MinimumValue { get; set; }

    public long MaximumValue { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public long Version { get; set; }
}

/// <summary>
/// 计数器分配结果：仅承载 LastValue 回读后的当前值，用于 SerialNumberAllocator 拼装最终 SerialNumber；
/// Value 小于 MinimumValue 表示 SequenceExhausted，必须返回 Conflict 而非自增。
/// </summary>
internal sealed class AllocatedCounterValue
{
    public long Value { get; set; }
}

/// <summary>
/// 流水号分配历史记录；用于幂等回放查询，按 (RuleId, TenantId, IdempotencyKey) 唯一定位已分配行。
/// </summary>
internal sealed class SerialNumberAllocationRecord
{
    public string RuleKey { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public long SequenceValue { get; set; }

    public string ResetBucket { get; set; } = string.Empty;

    public DateTimeOffset AllocatedAtUtc { get; set; }
}
