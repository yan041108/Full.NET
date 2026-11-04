using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Document;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Document;

/// <summary>验证独立选项容器与真实原生 Worker 在启动后取得数据库策略变更。</summary>
internal static class DocumentVersionRetentionRefreshAssertions
{
    internal static async Task VerifyIndependentMonitorAsync(FullNetApiFactory factory, HttpClient client, string token)
    {
        var apiMonitor = factory.Services.GetRequiredService<IOptionsMonitor<DocumentVersionRetentionOptions>>();
        using var readRequest = Request(HttpMethod.Get, token);
        using var read = await client.SendAsync(readRequest);
        Assert.AreEqual(HttpStatusCode.OK, read.StatusCode);
        var current = await read.Content.ReadFromJsonAsync<HostDocumentVersionRetentionSettingsResponse>();
        Assert.IsNotNull(current);
        Assert.AreEqual(current.BatchSize, apiMonitor.CurrentValue.BatchSize);

        await using var databaseScope = factory.Services.CreateAsyncScope();
        var writer = databaseScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var services = new ServiceCollection().AddLogging()
            .AddSingleton(writer)
            .AddSingleton(databaseScope.ServiceProvider.GetRequiredService<IQueryExecutor>())
            .AddSingleton(databaseScope.ServiceProvider.GetRequiredService<ICommandExecutor>())
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(Options.Create(new DatabaseOptions { Provider = factory.Provider }));
        new DocumentModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
        await using var workerProvider = services.BuildServiceProvider();
        var workerMonitor = workerProvider.GetRequiredService<IOptionsMonitor<DocumentVersionRetentionOptions>>();
        Assert.AreEqual(50, workerMonitor.CurrentValue.BatchSize);
        // 集成夹具只通过公开宿主接口加载另一容器；真实轮询由独立原生 Worker 场景验证。
        var loader = workerProvider.GetServices<IHostedService>().Single(static service => service is not BackgroundService);
        await loader.StartAsync(CancellationToken.None);
        Assert.AreEqual(current.BatchSize, workerMonitor.CurrentValue.BatchSize);

        using var update = Request(HttpMethod.Put, token);
        update.Content = JsonContent.Create(new UpdateHostDocumentVersionRetentionRequest(3, 0, 60, 77) { Version = current.Version });
        using var saved = await client.SendAsync(update);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        Assert.AreEqual(3, apiMonitor.CurrentValue.MinimumRetainedVersionsPerItem);
        Assert.AreEqual(77, apiMonitor.CurrentValue.BatchSize);
        Assert.AreEqual(current.BatchSize, workerMonitor.CurrentValue.BatchSize);
        await loader.StartAsync(CancellationToken.None);
        Assert.AreEqual(3, workerMonitor.CurrentValue.MinimumRetainedVersionsPerItem);
        Assert.AreEqual(77, workerMonitor.CurrentValue.BatchSize);
    }

    internal static async Task<Guid> PrepareNativeAsync(DatabaseProvider provider, string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = Connection(provider, connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var itemId = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        object timestamp = provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_document_version_retention_setting
                (Id, TenantId, MinimumRetainedVersionsPerItem, MaximumRetainedHistoryVersions, PollSeconds, BatchSize, Version, UpdatedAtUtc)
            VALUES (@Id, NULL, 4, 0, 60, 10, 1, @Timestamp)
            """, new { Id = Guid.Parse("01950000-0000-7000-8000-000000000232"), Timestamp = timestamp }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_document_item
                (Id, TenantId, CategoryId, CurrentVersionId, Title, Description, IsDeleted, DeletedAtUtc,
                 DeletedByUserId, CreatedAtUtc, CreatedByUserId, UpdatedAtUtc, UpdatedByUserId, Version)
            VALUES (@Id, NULL, NULL, NULL, 'Native retention refresh', NULL, 0, NULL, NULL, @Timestamp, @Actor, NULL, NULL, 1)
            """, new { Id = itemId, Timestamp = timestamp, Actor = actor }, transaction, cancellationToken: cancellationToken));
        Guid currentId = Guid.Empty;
        for (var number = 1; number <= 4; number++)
        {
            var fileId = Guid.CreateVersion7();
            currentId = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_files_file
                    (Id, TenantId, OriginalFileName, ContentType, SizeBytes, ProviderKey, StorageKey, ContentHash,
                     StorageState, CreatedAtUtc, CreatedByUserId, DeletedAtUtc)
                VALUES (@Id, NULL, 'retention-refresh.bin', 'application/octet-stream', 7, 'local', @StorageKey,
                        NULL, 'ready', @Timestamp, @Actor, NULL)
                """, new { Id = fileId, StorageKey = "retention-refresh/" + fileId.ToString("N"), Timestamp = timestamp, Actor = actor }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_document_version
                    (Id, DocumentItemId, FileId, VersionNumber, ContentHash, SizeBytes, UploadedByUserId, CreatedAtUtc)
                VALUES (@Id, @ItemId, @FileId, @Number, NULL, 7, @Actor, @Timestamp)
                """, new { Id = currentId, ItemId = itemId, FileId = fileId, Number = number, Actor = actor, Timestamp = timestamp }, transaction, cancellationToken: cancellationToken));
        }
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE fn_document_item SET CurrentVersionId = @CurrentId WHERE Id = @ItemId AND TenantId IS NULL
            """, new { CurrentId = currentId, ItemId = itemId }, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return itemId;
    }

    internal static async Task EnableNativeAndVerifyTrimAsync(DatabaseProvider provider, string connectionString,
        Guid itemId, string logPath, CancellationToken cancellationToken)
    {
        await using var connection = Connection(provider, connectionString);
        await connection.OpenAsync(cancellationToken);
        Assert.AreEqual(4, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM fn_document_version WHERE DocumentItemId = @ItemId", new { ItemId = itemId }, cancellationToken: cancellationToken)));
        object timestamp = provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
        var changed = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE fn_document_version_retention_setting
            SET MinimumRetainedVersionsPerItem = 2, MaximumRetainedHistoryVersions = 1, Version = Version + 1, UpdatedAtUtc = @Timestamp
            WHERE Id = @Id AND TenantId IS NULL AND Version = 1
            """, new { Id = Guid.Parse("01950000-0000-7000-8000-000000000232"), Timestamp = timestamp }, cancellationToken: cancellationToken));
        Assert.AreEqual(1, changed);

        var elapsed = Stopwatch.StartNew();
        var count = 4;
        while (elapsed.Elapsed < TimeSpan.FromSeconds(130))
        {
            count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(1) FROM fn_document_version WHERE DocumentItemId = @ItemId", new { ItemId = itemId }, cancellationToken: cancellationToken));
            if (count == 2) break;
            await Task.Delay(200, cancellationToken);
        }
        Assert.AreEqual(2, count, $"Worker 未取得启动后启用的策略。日志：{logPath}");
        var remaining = (await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT VersionNumber FROM fn_document_version WHERE DocumentItemId = @ItemId ORDER BY VersionNumber", new { ItemId = itemId }, cancellationToken: cancellationToken))).ToArray();
        CollectionAssert.AreEqual(new[] { 3, 4 }, remaining);
        var audited = (await connection.QueryAsync<int>(new CommandDefinition("""
            SELECT VersionNumber FROM fn_document_version_deletion_audit
            WHERE DocumentItemId = @ItemId AND DeletedBySourceKey = 'retention' ORDER BY VersionNumber
            """, new { ItemId = itemId }, cancellationToken: cancellationToken))).ToArray();
        CollectionAssert.AreEqual(new[] { 1, 2 }, audited);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(1) FROM fn_document_item AS i INNER JOIN fn_document_version AS v ON v.Id = i.CurrentVersionId
            WHERE i.Id = @ItemId AND i.TenantId IS NULL AND v.DocumentItemId = i.Id AND v.VersionNumber = 4
            """, new { ItemId = itemId }, cancellationToken: cancellationToken)));
    }

    private static HttpRequestMessage Request(HttpMethod method, string token)
    {
        var request = new HttpRequestMessage(method, "/api/v1/document/host/version-retention");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.MySql
        ? new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
        : new SqlConnection(connectionString);
}
