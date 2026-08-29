using System.Text.Json;
using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>验证原生 Worker 的双库启动、Dapper 读取、源生成 JSON 与正常退出链路。</summary>
internal static class NativeWorkerE2EAssertions
{
    private const string MessageType =
        "fullnet.notifications.announcement.published";

    private static readonly string[] FatalMarkers =
    [
        "MissingMethodException",
        "TypeInitializationException",
        "JsonSerializerIsReflectionDisabled",
        "IL3050",
        "IL2026",
    ];

    public static async Task VerifyVersionRetirementAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        var result = await NativeWorkerProcessRunner.RunVersionRetirementAsync(
                artifact,
                provider,
                connectionString,
                MessageType,
                1,
                TimeSpan.FromMinutes(2),
                cancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(
            0,
            result.ExitCode,
            $"Native Worker 退出失败。日志：{result.LogPath}\n{result.StandardError}");
        var jsonLine = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.TrimStart().StartsWith('{'));
        Assert.IsNotNull(jsonLine, $"Native Worker 未输出 JSON。日志：{result.LogPath}");
        using var payload = JsonDocument.Parse(jsonLine);
        Assert.AreEqual(
            "outbox.version_retirement.safe",
            payload.RootElement.GetProperty("code").GetString());
        Assert.AreEqual(MessageType, payload.RootElement.GetProperty("messageType").GetString());
        Assert.AreEqual(1, payload.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(0, payload.RootElement.GetProperty("pendingCount").GetInt64());
        Assert.AreEqual(0, payload.RootElement.GetProperty("deadLetterCount").GetInt64());

        var combinedOutput = result.StandardOutput + result.StandardError;
        foreach (var marker in FatalMarkers)
        {
            Assert.IsFalse(
                combinedOutput.Contains(marker, StringComparison.Ordinal),
                $"Native Worker 日志包含 AOT 致命标记 {marker}：{result.LogPath}");
        }
    }

    public static async Task VerifyPersistentRuntimeAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        var heartbeatCountBefore = await ReadJobsHeartbeatCountAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        await using var host = await NativeWorkerProcessHost.StartAsync(
                artifact,
                provider,
                connectionString,
                TimeSpan.FromMinutes(2),
                cancellationToken)
            .ConfigureAwait(false);

        await WaitForJobsHeartbeatAsync(
                provider,
                connectionString,
                heartbeatCountBefore,
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
            .ConfigureAwait(false);
        await host.StopGracefullyAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken)
            .ConfigureAwait(false);

        Assert.AreEqual(
            0,
            host.ExitCode,
            $"Native Worker 未正常响应 SIGTERM。日志：{host.LogFilePath}");
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyLegacyOutboxDeliveryAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        var messages = await NativeWorkerOutboxProbe.EnqueueAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        await using var host = await NativeWorkerProcessHost.StartAsync(
                artifact,
                provider,
                connectionString,
                TimeSpan.FromMinutes(2),
                cancellationToken)
            .ConfigureAwait(false);

        var states = await NativeWorkerOutboxProbe.WaitForTerminalStatesAsync(
                provider,
                connectionString,
                messages,
                TimeSpan.FromSeconds(30),
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(1, states.Valid.Attempts);
        Assert.AreEqual(1L, states.Valid.IsProcessed);
        Assert.AreEqual(0L, states.Valid.IsDeadLettered);
        Assert.IsNull(states.Valid.DeadLetterReasonCode);
        Assert.AreEqual(1L, states.Valid.IsLeaseReleased);
        Assert.AreEqual(1L, states.Valid.IsRetryCleared);

        Assert.AreEqual(1, states.Invalid.Attempts);
        Assert.AreEqual(0L, states.Invalid.IsProcessed);
        Assert.AreEqual(1L, states.Invalid.IsDeadLettered);
        Assert.AreEqual(
            OutboxDeadLetterReasons.InvalidPayload,
            states.Invalid.DeadLetterReasonCode);
        Assert.AreEqual(1L, states.Invalid.IsLeaseReleased);
        Assert.AreEqual(1L, states.Invalid.IsRetryCleared);

        await host.StopGracefullyAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(
            0,
            host.ExitCode,
            $"Native Worker 未正常响应 SIGTERM。日志：{host.LogFilePath}");
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyJobsPingExecutionAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        var executionId = await NativeWorkerJobsProbe.EnqueuePingAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        await using var host = await NativeWorkerProcessHost.StartAsync(
                artifact,
                provider,
                connectionString,
                TimeSpan.FromMinutes(2),
                cancellationToken)
            .ConfigureAwait(false);

        var state = await NativeWorkerJobsProbe.WaitForTerminalAsync(
                provider,
                connectionString,
                executionId,
                TimeSpan.FromSeconds(30),
                host.LogFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(JobExecutionStatuses.Succeeded, state.Status);
        Assert.AreEqual(1, state.AttemptCount);
        Assert.IsNull(state.ErrorMessage);
        Assert.AreEqual(1L, state.IsStarted);
        Assert.AreEqual(1L, state.IsFinished);
        Assert.AreEqual(1L, state.IsChronological);
        Assert.AreEqual(1L, state.IsLeaseReleased);
        Assert.AreEqual(1L, state.IsRetryCleared);

        await host.StopGracefullyAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken)
            .ConfigureAwait(false);
        Assert.AreEqual(
            0,
            host.ExitCode,
            $"Native Worker 未正常响应 SIGTERM。日志：{host.LogFilePath}");
        host.AssertNoFatalMarkersInLogs();
    }

    public static async Task VerifyFilesUploadReconciliationAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeApiDatabaseBootstrap.BootstrapAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        var scenario = await NativeWorkerFilesProbe.PrepareAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using var host = await NativeWorkerProcessHost.StartAsync(
                    artifact,
                    provider,
                    connectionString,
                    TimeSpan.FromMinutes(2),
                    cancellationToken,
                    scenario.StorageRoot)
                .ConfigureAwait(false);
            var states = await NativeWorkerFilesProbe.WaitForTerminalStatesAsync(
                    provider,
                    connectionString,
                    scenario,
                    TimeSpan.FromSeconds(30),
                    host.LogFilePath,
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(1, states.IsExistingReady);
            Assert.AreEqual(0, states.IsMissingPresent);
            Assert.IsTrue(
                File.Exists(scenario.ExistingBlobPath),
                "Files 对账提升元数据时不得删除已经存在的本地 Blob。");

            await host.StopGracefullyAsync(
                    TimeSpan.FromSeconds(30),
                    cancellationToken)
                .ConfigureAwait(false);
            Assert.AreEqual(
                0,
                host.ExitCode,
                $"Native Worker 未正常响应 SIGTERM。日志：{host.LogFilePath}");
            host.AssertNoFatalMarkersInLogs();
        }
        finally
        {
            NativeWorkerFilesProbe.DeleteOwnedStorageRoot(scenario.StorageRoot);
        }
    }

    private static async Task WaitForJobsHeartbeatAsync(
        DatabaseProvider provider,
        string connectionString,
        long heartbeatCountBefore,
        string logFilePath,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await ReadJobsHeartbeatCountAsync(
                    provider,
                    connectionString,
                    cancellationToken)
                .ConfigureAwait(false);
            if (count > heartbeatCountBefore)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        Assert.Fail(
            $"Native Worker 未在 30 秒内写入 Jobs 心跳。日志：{logFilePath}");
    }

    private static async Task<long> ReadJobsHeartbeatCountAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using DbConnection connection = provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(connectionString),
            _ => throw new InvalidOperationException(
                $"Unsupported database provider '{provider}'."),
        };
        return await connection.QuerySingleAsync<long>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM fn_jobs_worker_instance WHERE TenantId IS NULL",
                    cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
