using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>
/// 验证审计保留清理在双数据库中遵守小批量、公平轮转和严格截止时间边界。
/// </summary>
internal static class AuditingRetentionAssertions
{
    public static async Task VerifyAsync(
        FullNetApiFactory factory,
        CancellationToken cancellationToken = default)
    {
        await factory.InitializeAsync(cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var currentTenant = services.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();

        try
        {
            var now = services.GetRequiredService<IClock>().UtcNow;
            var traceId = $"retention-{Guid.NewGuid():N}";
            await InsertFixturesAsync(
                services.GetRequiredService<ICommandExecutor>(),
                now,
                traceId,
                cancellationToken);

            var runner = new AuditingRetentionRunner(
                services.GetRequiredService<IQueryExecutor>(),
                services.GetRequiredService<ICommandExecutor>(),
                services.GetRequiredService<ICommandTransaction>(),
                services.GetRequiredService<IClock>(),
                services.GetRequiredService<IOptions<DatabaseOptions>>());
            var first = await runner.RunOnceAsync(
                CreateOptions(3),
                cancellationToken);

            Assert.AreEqual(1, first.AccessDeleted);
            Assert.AreEqual(1, first.OperationDeleted);
            Assert.AreEqual(1, first.ExceptionDeleted);
            Assert.AreEqual(0, first.OutboundDeleted);
            Assert.AreEqual(3, first.BatchesExecuted);
            Assert.AreEqual(
                new RetentionCounts { OldCount = 5, FreshCount = 4 },
                await ReadCountsAsync(
                    services.GetRequiredService<IQueryExecutor>(),
                    traceId,
                    now.AddDays(-30),
                    cancellationToken));

            var second = await runner.RunOnceAsync(
                CreateOptions(10),
                cancellationToken);

            Assert.AreEqual(5, second.TotalDeleted);
            Assert.AreEqual(
                new RetentionCounts { OldCount = 0, FreshCount = 4 },
                await ReadCountsAsync(
                    services.GetRequiredService<IQueryExecutor>(),
                    traceId,
                    now.AddDays(-30),
                    cancellationToken));

            await VerifyDetailsCleanupAsync(services, now, cancellationToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    private static AuditingRetentionOptions CreateOptions(int maxBatchesPerRun) =>
        new()
        {
            Enabled = true,
            AccessRetentionDays = 30,
            OperationRetentionDays = 30,
            ExceptionRetentionDays = 30,
            OutboundRetentionDays = 30,
            BatchSize = 1,
            MaxBatchesPerRun = maxBatchesPerRun,
        };

    private static async Task VerifyDetailsCleanupAsync(
        IServiceProvider services,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = services.GetRequiredService<ICommandExecutor>();
        var query = services.GetRequiredService<IQueryExecutor>();
        var traceId = $"details-retention-{Guid.NewGuid():N}";
        await command.ExecuteAsync(
            new SqlStatement(
                "test.auditing.details_retention.insert_fixtures",
                """
                INSERT INTO fn_auditing_operation_log
                    (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath,
                     StatusCode, DurationMs, Succeeded, UserId, TenantId,
                     TraceId, ClientIpFingerprint, PermissionCode, ContextJson,
                     DetailsExpiresAtUtc)
                VALUES
                    (@FirstId, @NowUtc, 'details.expired.first', 'POST',
                     '/details/expired/first', 200, 1, 1, NULL, NULL,
                     @TraceId, NULL, NULL, '{"v":1}', @ExpiredAtUtc),
                    (@SecondId, @NowUtc, 'details.expired.second', 'POST',
                     '/details/expired/second', 200, 1, 1, NULL, NULL,
                     @TraceId, NULL, NULL, '{"v":1}', @ExpiredAtUtc),
                    (@FreshId, @NowUtc, 'details.fresh', 'POST',
                     '/details/fresh', 200, 1, 1, NULL, NULL,
                     @TraceId, NULL, NULL, '{"v":1}', @FreshAtUtc)
                """,
                SqlDataScope.HostOnly),
            new
            {
                FirstId = Guid.CreateVersion7(),
                SecondId = Guid.CreateVersion7(),
                FreshId = Guid.CreateVersion7(),
                NowUtc = now,
                TraceId = traceId,
                ExpiredAtUtc = now.AddMinutes(-1),
                FreshAtUtc = now.AddDays(1),
            },
            cancellationToken);

        var runner = new AuditDetailsRetentionRunner(
            query,
            command,
            services.GetRequiredService<ICommandTransaction>(),
            services.GetRequiredService<IClock>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>());
        var options = new AuditDetailsRetentionOptions
        {
            BatchSize = 1,
            MaxBatchesPerRun = 1,
        };
        var first = await runner.RunOnceAsync(options, cancellationToken);
        Assert.AreEqual(1, first.Cleared);
        Assert.IsTrue(first.MayHaveMore);
        var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        await Task.WhenAll(
            RecordCheckpointInIndependentScopeAsync(scopeFactory, cancellationToken),
            RecordCheckpointInIndependentScopeAsync(scopeFactory, cancellationToken));
        var checkpointStore = new AuditDetailsCleanupCheckpointStore(
            query,
            command,
            services.GetRequiredService<ICommandTransaction>(),
            services.GetRequiredService<IClock>(),
            services.GetRequiredService<IIdGenerator>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>());
        var checkpoint = await checkpointStore.RecordSuccessfulPassAsync(cancellationToken);
        Assert.IsNotNull(checkpoint.OldestExpiredAtUtc);
        Assert.IsTrue(checkpoint.OldestExpiredAtUtc <= now);
        var persistedBacklog = await checkpointStore.ReadAsync(cancellationToken);
        Assert.IsNotNull(persistedBacklog);
        Assert.IsNotNull(persistedBacklog.Value.OldestExpiredAtUtc);
        var staleStore = new AuditDetailsCleanupCheckpointStore(
            query,
            command,
            services.GetRequiredService<ICommandTransaction>(),
            new FrozenClock(checkpoint.LastSuccessfulCleanupAtUtc.AddMinutes(-5)),
            services.GetRequiredService<IIdGenerator>(),
            services.GetRequiredService<IOptions<DatabaseOptions>>());
        await staleStore.RecordSuccessfulPassAsync(cancellationToken);
        var preservedBacklogRows = await query.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.auditing.details_retention.read_preserved_checkpoint",
                """
                SELECT COUNT(*) FROM fn_auditing_details_cleanup_state
                WHERE StateKey = 1
                  AND LastSuccessfulCleanupAtUtc >= @MinimumSuccessUtc
                  AND OldestExpiredAtUtc IS NOT NULL
                """,
                SqlDataScope.HostOnly),
            new { MinimumSuccessUtc = checkpoint.LastSuccessfulCleanupAtUtc.AddSeconds(-1) },
            cancellationToken: cancellationToken);
        Assert.AreEqual(1L, preservedBacklogRows);

        var second = await runner.RunOnceAsync(options, cancellationToken);
        Assert.AreEqual(1, second.Cleared);
        var third = await runner.RunOnceAsync(options, cancellationToken);
        Assert.AreEqual(0, third.Cleared);
        Assert.IsFalse(third.MayHaveMore);
        var caughtUp = await checkpointStore.RecordSuccessfulPassAsync(cancellationToken);
        Assert.IsNull(caughtUp.OldestExpiredAtUtc);
        var persistedCaughtUp = await checkpointStore.ReadAsync(cancellationToken);
        Assert.IsNotNull(persistedCaughtUp);
        Assert.IsNull(persistedCaughtUp.Value.OldestExpiredAtUtc);
        var caughtUpRows = await query.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.auditing.details_retention.read_caught_up_checkpoint",
                """
                SELECT COUNT(*) FROM fn_auditing_details_cleanup_state
                WHERE StateKey = 1 AND OldestExpiredAtUtc IS NULL
                """,
                SqlDataScope.HostOnly),
            cancellationToken: cancellationToken);
        Assert.AreEqual(1L, caughtUpRows);

        var remaining = await query.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.auditing.details_retention.read_fixture_counts",
                """
                SELECT COUNT(*) FROM fn_auditing_operation_log
                WHERE TraceId = @TraceId
                  AND (
                      (ActionKey LIKE 'details.expired.%'
                       AND ContextJson IS NULL AND DetailsExpiresAtUtc IS NULL)
                      OR (ActionKey = 'details.fresh'
                          AND ContextJson IS NOT NULL
                          AND DetailsExpiresAtUtc > @NowUtc)
                  )
                """,
                SqlDataScope.HostOnly),
            new { TraceId = traceId, NowUtc = now },
            cancellationToken);
        Assert.AreEqual(3L, remaining);
    }

    private static async Task RecordCheckpointInIndependentScopeAsync(
        IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var tenant = services.GetRequiredService<ICurrentTenantContextWriter>();
        tenant.SetHost();
        try
        {
            var store = new AuditDetailsCleanupCheckpointStore(
                services.GetRequiredService<IQueryExecutor>(),
                services.GetRequiredService<ICommandExecutor>(),
                services.GetRequiredService<ICommandTransaction>(),
                services.GetRequiredService<IClock>(),
                services.GetRequiredService<IIdGenerator>(),
                services.GetRequiredService<IOptions<DatabaseOptions>>());
            await store.RecordSuccessfulPassAsync(cancellationToken);
        }
        finally
        {
            tenant.Clear();
        }
    }

    private static async Task InsertFixturesAsync(
        ICommandExecutor command,
        DateTimeOffset now,
        string traceId,
        CancellationToken cancellationToken)
    {
        var oldFirst = now.AddDays(-400);
        var oldSecond = now.AddDays(-399);
        await command.ExecuteAsync(
            new SqlStatement(
                "test.auditing.retention.insert_access_fixtures",
                """
                INSERT INTO fn_auditing_access_log
                    (Id, OccurredAtUtc, HttpMethod, RequestPath, StatusCode,
                     DurationMs, UserId, TenantId, TraceId, ClientIpFingerprint,
                     IsAuthenticated)
                VALUES
                    (@OldFirstId, @OldFirst, 'GET', '/retention/old-1', 200,
                     1, NULL, NULL, @TraceId, NULL, 0),
                    (@OldSecondId, @OldSecond, 'GET', '/retention/old-2', 200,
                     1, NULL, NULL, @TraceId, NULL, 0),
                    (@FreshId, @Fresh, 'GET', '/retention/fresh', 200,
                     1, NULL, NULL, @TraceId, NULL, 0)
                """,
                SqlDataScope.HostOnly),
            new
            {
                OldFirstId = Guid.CreateVersion7(),
                OldSecondId = Guid.CreateVersion7(),
                FreshId = Guid.CreateVersion7(),
                OldFirst = oldFirst,
                OldSecond = oldSecond,
                Fresh = now,
                TraceId = traceId,
            },
            cancellationToken);
        await command.ExecuteAsync(
            new SqlStatement(
                "test.auditing.retention.insert_operation_fixtures",
                """
                INSERT INTO fn_auditing_operation_log
                    (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath,
                     StatusCode, DurationMs, Succeeded, UserId, TenantId,
                     TraceId, ClientIpFingerprint, PermissionCode)
                VALUES
                    (@OldFirstId, @OldFirst, 'retention.old-1', 'POST',
                     '/retention/old-1', 200, 1, 1, NULL, NULL, @TraceId, NULL, NULL),
                    (@OldSecondId, @OldSecond, 'retention.old-2', 'POST',
                     '/retention/old-2', 200, 1, 1, NULL, NULL, @TraceId, NULL, NULL),
                    (@FreshId, @Fresh, 'retention.fresh', 'POST',
                     '/retention/fresh', 200, 1, 1, NULL, NULL, @TraceId, NULL, NULL)
                """,
                SqlDataScope.HostOnly),
            new
            {
                OldFirstId = Guid.CreateVersion7(),
                OldSecondId = Guid.CreateVersion7(),
                FreshId = Guid.CreateVersion7(),
                OldFirst = oldFirst,
                OldSecond = oldSecond,
                Fresh = now,
                TraceId = traceId,
            },
            cancellationToken);
        await command.ExecuteAsync(
            new SqlStatement(
                "test.auditing.retention.insert_exception_fixtures",
                """
                INSERT INTO fn_auditing_exception_log
                    (Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
                     HttpMethod, RequestPath, UserId, TenantId, TraceId,
                     ClientIpFingerprint)
                VALUES
                    (@OldFirstId, @OldFirst, 'RetentionOld', 'old-1', NULL,
                     'GET', '/retention/old-1', NULL, NULL, @TraceId, NULL),
                    (@OldSecondId, @OldSecond, 'RetentionOld', 'old-2', NULL,
                     'GET', '/retention/old-2', NULL, NULL, @TraceId, NULL),
                    (@FreshId, @Fresh, 'RetentionFresh', 'fresh', NULL,
                     'GET', '/retention/fresh', NULL, NULL, @TraceId, NULL)
                """,
                SqlDataScope.HostOnly),
            new
            {
                OldFirstId = Guid.CreateVersion7(),
                OldSecondId = Guid.CreateVersion7(),
                FreshId = Guid.CreateVersion7(),
                OldFirst = oldFirst,
                OldSecond = oldSecond,
                Fresh = now,
                TraceId = traceId,
            },
            cancellationToken);
        await command.ExecuteAsync(
            new SqlStatement(
                "test.auditing.retention.insert_outbound_fixtures",
                """
                INSERT INTO fn_auditing_outbound_call
                    (Id, OccurredAtUtc, ProviderKey, OperationKey, DestinationHostCategory,
                     StatusCode, Succeeded, DurationMs, RetryCount, TraceId, SafeErrorCode,
                     TenantId, UserId)
                VALUES
                    (@OldFirstId, @OldFirst, 'retention.probe', 'old-1', 'host.old-1',
                     500, 0, 1, 0, @TraceId, 'retention.old', NULL, NULL),
                    (@OldSecondId, @OldSecond, 'retention.probe', 'old-2', 'host.old-2',
                     500, 0, 1, 0, @TraceId, 'retention.old', NULL, NULL),
                    (@FreshId, @Fresh, 'retention.probe', 'fresh', 'host.fresh',
                     200, 1, 1, 0, @TraceId, NULL, NULL, NULL)
                """,
                SqlDataScope.HostOnly),
            new
            {
                OldFirstId = Guid.CreateVersion7(),
                OldSecondId = Guid.CreateVersion7(),
                FreshId = Guid.CreateVersion7(),
                OldFirst = oldFirst,
                OldSecond = oldSecond,
                Fresh = now,
                TraceId = traceId,
            },
            cancellationToken);
    }

    private static async Task<RetentionCounts> ReadCountsAsync(
        IQueryExecutor query,
        string traceId,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        var counts = await query.QuerySingleOrDefaultAsync<RetentionCounts>(
            new SqlStatement(
                "test.auditing.retention.read_fixture_counts",
                """
                SELECT
                    (SELECT COUNT(*) FROM fn_auditing_access_log
                     WHERE TraceId = @TraceId AND OccurredAtUtc < @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_auditing_operation_log
                       WHERE TraceId = @TraceId AND OccurredAtUtc < @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_auditing_exception_log
                       WHERE TraceId = @TraceId AND OccurredAtUtc < @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_auditing_outbound_call
                       WHERE TraceId = @TraceId AND OccurredAtUtc < @CutoffUtc)
                        AS OldCount,
                    (SELECT COUNT(*) FROM fn_auditing_access_log
                     WHERE TraceId = @TraceId AND OccurredAtUtc >= @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_auditing_operation_log
                       WHERE TraceId = @TraceId AND OccurredAtUtc >= @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_auditing_exception_log
                       WHERE TraceId = @TraceId AND OccurredAtUtc >= @CutoffUtc)
                    + (SELECT COUNT(*) FROM fn_auditing_outbound_call
                       WHERE TraceId = @TraceId AND OccurredAtUtc >= @CutoffUtc)
                        AS FreshCount
                """,
                SqlDataScope.HostOnly),
            new { TraceId = traceId, CutoffUtc = cutoffUtc },
            cancellationToken);
        return counts
            ?? throw new InvalidOperationException(
                "Audit retention fixture counts were not returned.");
    }

    private sealed record RetentionCounts
    {
        public long OldCount { get; set; }

        public long FreshCount { get; set; }
    }

    private sealed class FrozenClock(DateTimeOffset value) : IClock
    {
        public DateTimeOffset UtcNow => value;
    }
}
