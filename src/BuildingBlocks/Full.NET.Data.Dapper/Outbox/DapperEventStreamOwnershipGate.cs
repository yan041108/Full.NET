using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 事件流所有权门禁（Event Stream Ownership Gate），通过数据库行级锁 + PreviousOwner CAS
/// （Compare-And-Swap）机制，在业务事务内强制同步 <c>fn_messaging_stream_ownership</c> 表，
/// 防止切流过程中 LegacyPolling 与 CdcKafka 两个交付通道同时产出同一事件流造成重复投递。
/// </summary>
/// <remarks>
/// <para><b>数据库行锁不变量（Row Lock Invariant）：</b>
/// 所有 Acquire 方法必须在活动事务内调用（由 <see cref="IDbTransactionCoordinator.HasTransaction"/> 门禁）。
/// SQL Server 使用 WITH (HOLDLOCK) 保证可重复读语义下的范围锁；
/// MySQL 使用 FOR SHARE / FOR UPDATE 读取并持有行锁直到事务提交/回滚，
/// 并发事务在同一 (MessageType, SchemaVersion) 行上会排队等待，天然串行化所有权变更。</para>
/// <para><b>CAS 语义（Compare-And-Swap + PreviousOwner）：</b>
/// 所有权变更（LegacyPolling → ShadowCdc → CdcKafka）不是直接 UPDATE，而是通过
/// "事务开始时 HOLDLOCK 读取 CurrentOwner → 业务逻辑判断 → 仅当 CurrentOwner 匹配期望值时 UPDATE"
/// 两步实现。虽然本类只负责读阶段（写阶段由 Messaging 模块编排的事务脚本执行），
/// 但 HOLDLOCK 确保读-写之间行版本未被其他会话修改，等价于 Compare-And-Swap 的原子性。</para>
/// <para><b>RollbackState 护栏（-1 哨兵值）：</b>
/// Producer 路径在读取时若检测到 RollbackState = 1（切流回滚准备中），返回 -1 并抛出
/// <see cref="EventDeliveryProducerFencedException"/>，拒绝业务写入，
/// 防止 Rollback 窗口内新消息写入旧表而遗漏 CDC 捕获。
/// Consumer 路径（AcquireConsumerAsync）不检查 RollbackState，以便回滚侧继续消费。</para>
/// <para><b>锁级别区分：</b>
/// <list type="bullet">
/// <item>Producer / Consumer：共享锁（HOLDLOCK / FOR SHARE），允许多生产者并发写入同一条事件流。</item>
/// <item>OwnershipChange：排他更新锁（UPDLOCK, HOLDLOCK / FOR UPDATE），用于切流编排事务。</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class DapperEventStreamOwnershipGate(
    IQueryExecutor queryExecutor,
    IDbTransactionCoordinator transactionCoordinator,
    IOptions<DatabaseOptions> databaseOptions) : IEventStreamOwnershipGate
{
    /// <summary>
    /// 生产者侧门禁：在事务内以共享锁读取事件流所有权，校验 RollbackState 未处于回滚准备中。
    /// 用于 <see cref="DapperRoutedOutboxWriter"/> 写入前的同步点。
    /// </summary>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>true 表示所有权行存在且已取得共享锁；false 表示行不存在（未注册事件流）。</returns>
    /// <exception cref="EventDeliveryProducerFencedException">当 RollbackState=1 时抛出，拒绝写入。</exception>
    public Task<bool> AcquireProducerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(
            eventType,
            schemaVersion,
            exclusive: false,
            rejectRollbackPreparing: true,
            cancellationToken);

    /// <summary>
    /// 消费者侧门禁：读取事件流所有权并返回是否存在。不检查 RollbackState，不阻塞回滚流程。
    /// </summary>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>true 表示所有权行存在；false 表示流未注册。</returns>
    public async Task<bool> AcquireConsumerAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        var fence = await AcquireConsumerFenceAsync(
                eventType,
                schemaVersion,
                cancellationToken)
            .ConfigureAwait(false);
        return fence.OwnershipExists;
    }

    /// <summary>
    /// 消费者侧门禁扩展：在事务内读取事件流所有权，并返回携带具体 Owner 枚举的结构化结果。
    /// 相比 <see cref="AcquireConsumerAsync"/>，额外返回 Missing / Acquired(Owner) 区分。
    /// </summary>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// <see cref="EventStreamConsumerFenceResult.Missing"/> 或
    /// <see cref="EventStreamConsumerFenceResult.Acquired(EventDeliveryOwner)"/>。
    /// </returns>
    public async Task<EventStreamConsumerFenceResult> AcquireConsumerFenceAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateArgumentsAndTransaction(eventType, schemaVersion);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => ConsumerSqlServer,
            DatabaseProvider.MySql => ConsumerMySql,
            _ => throw new NotSupportedException(
                $"Database provider '{databaseOptions.Value.Provider}' is not supported."),
        };
        var owner = await queryExecutor.QuerySingleOrDefaultAsync<int?>(
            statement,
            CreateParameters(eventType, schemaVersion),
            cancellationToken).ConfigureAwait(false);
        if (!owner.HasValue)
        {
            return EventStreamConsumerFenceResult.Missing;
        }

        if (!Enum.IsDefined(typeof(EventDeliveryOwner), owner.Value))
        {
            throw new InvalidOperationException(
                $"Event stream ownership row contains unsupported owner '{owner.Value}'.");
        }

        return EventStreamConsumerFenceResult.Acquired((EventDeliveryOwner)owner.Value);
    }

    /// <summary>
    /// 所有权变更编排门禁：以排他更新锁（UPDLOCK / FOR UPDATE）读取所有权行，
    /// 用于切流编排事务（LegacyPolling → ShadowCdc → CdcKafka 或反向回滚）。
    /// 排他锁保证同一时刻只有一个编排事务可以修改该事件流的所有权，等价于 CAS 的 Read-Phase。
    /// </summary>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">Schema 版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>true 表示行存在且已取得排他更新锁；false 表示行不存在。</returns>
    public Task<bool> AcquireOwnershipChangeAsync(
        string eventType,
        int schemaVersion,
        CancellationToken cancellationToken = default) =>
        AcquireAsync(
            eventType,
            schemaVersion,
            exclusive: true,
            rejectRollbackPreparing: false,
            cancellationToken);

    private async Task<bool> AcquireAsync(
        string eventType,
        int schemaVersion,
        bool exclusive,
        bool rejectRollbackPreparing,
        CancellationToken cancellationToken)
    {
        ValidateArgumentsAndTransaction(eventType, schemaVersion);

        var statement = (databaseOptions.Value.Provider, exclusive) switch
        {
            (DatabaseProvider.SqlServer, false) => ProducerSqlServer,
            (DatabaseProvider.SqlServer, true) => OwnershipChangeSqlServer,
            (DatabaseProvider.MySql, false) => ProducerMySql,
            (DatabaseProvider.MySql, true) => OwnershipChangeMySql,
            _ => throw new NotSupportedException(
                $"Database provider '{databaseOptions.Value.Provider}' is not supported."),
        };
        var owner = await queryExecutor.QuerySingleOrDefaultAsync<int?>(
            statement,
            CreateParameters(eventType, schemaVersion),
            cancellationToken).ConfigureAwait(false);
        if (rejectRollbackPreparing && owner == -1)
        {
            throw new EventDeliveryProducerFencedException(eventType, schemaVersion);
        }

        return owner.HasValue;
    }

    private static IReadOnlyDictionary<string, object?> CreateParameters(
        string eventType,
        int schemaVersion) =>
        new Dictionary<string, object?>
        {
            ["MessageType"] = eventType,
            ["SchemaVersion"] = schemaVersion,
        };

    private void ValidateArgumentsAndTransaction(string eventType, int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (!transactionCoordinator.HasTransaction)
        {
            throw new InvalidOperationException(
                "Event stream ownership gates require an active database transaction.");
        }
    }

    private static readonly SqlStatement ConsumerSqlServer = new(
        "messaging.stream_ownership_gate.consumer.sql_server",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership WITH (HOLDLOCK)
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement ConsumerMySql = new(
        "messaging.stream_ownership_gate.consumer.my_sql",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        FOR SHARE
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement ProducerSqlServer = new(
        "messaging.stream_ownership_gate.producer.sql_server",
        """
        SELECT CASE WHEN RollbackState = 1 THEN -1 ELSE CurrentOwner END
        FROM fn_messaging_stream_ownership WITH (HOLDLOCK)
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement OwnershipChangeSqlServer = new(
        "messaging.stream_ownership_gate.change.sql_server",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership WITH (UPDLOCK, HOLDLOCK)
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement ProducerMySql = new(
        "messaging.stream_ownership_gate.producer.my_sql",
        """
        SELECT CASE WHEN RollbackState = 1 THEN -1 ELSE CurrentOwner END
        FROM fn_messaging_stream_ownership
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        FOR SHARE
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement OwnershipChangeMySql = new(
        "messaging.stream_ownership_gate.change.my_sql",
        """
        SELECT CurrentOwner
        FROM fn_messaging_stream_ownership
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion
        FOR UPDATE
        """,
        SqlDataScope.Global);
}
