using Full.NET.Abstractions.Messaging;
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
}
