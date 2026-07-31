using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.SerialNumbers.Persistence;

internal static class SerialNumberSql
{
    public static readonly SqlStatement PageRulesSqlServer = new(
        "serial_numbers.rule.page.sql_server",
        """
        SELECT COUNT(*) FROM fn_serialnumbers_rule;
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule
        ORDER BY DisplayOrder, RuleKey
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PageRulesMySql = new(
        "serial_numbers.rule.page.my_sql",
        """
        SELECT COUNT(*) FROM fn_serialnumbers_rule;
        SELECT Id, RuleKey, DisplayName, Description, Scope, ResetInterval,
               Pattern, MinimumValue, MaximumValue, DisplayOrder, IsEnabled,
               CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId,
               Version
        FROM fn_serialnumbers_rule
        ORDER BY DisplayOrder, RuleKey
        LIMIT @PageSize OFFSET @Offset;
        """,
        SqlDataScope.HostOnly);

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

    public static readonly SqlStatement CountAllocationsByRule = new(
        "serial_numbers.allocation.count_by_rule",
        """
        SELECT COUNT(*)
        FROM fn_serialnumbers_allocation
        WHERE RuleId = @RuleId
        """,
        SqlDataScope.HostOnly);

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

internal sealed class AllocatedCounterValue
{
    public long Value { get; set; }
}

internal sealed class SerialNumberAllocationRecord
{
    public string RuleKey { get; set; } = string.Empty;

    public string SerialNumber { get; set; } = string.Empty;

    public long SequenceValue { get; set; }

    public string ResetBucket { get; set; } = string.Empty;

    public DateTimeOffset AllocatedAtUtc { get; set; }
}
