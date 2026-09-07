using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Files.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Files;

/// <summary>使用生产租户资源文件 SQL 验证所有权谓词、跨租户隔离与错误资源不能释放。</summary>
internal static class TenantResourceFilePersistenceAssertions
{
    /// <summary>读取、发布和释放都必须同时匹配租户、所属模块和资源；错任一条件不得改写行。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Owned_access_requires_tenant_module_and_resource_async(DatabaseProvider provider)
    {
        var (connection, unused, tenantA, tenantB, resourceA, resourceB, fileId) =
            await SeedPendingPairAsync(provider).ConfigureAwait(false);
        await using var first = connection;
        await using var second = unused;
        var owner = "import_export";
        Assert.IsNull(await first.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
            TenantResourceFileSql.FindOwned.Text,
            new { TenantId = tenantB, Id = fileId, OwnerModuleKey = owner, ResourceId = resourceA }).ConfigureAwait(false));
        Assert.IsNull(await first.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
            TenantResourceFileSql.FindOwned.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = "reporting", ResourceId = resourceA }).ConfigureAwait(false));
        Assert.IsNull(await first.QuerySingleOrDefaultAsync<TenantResourceFileRecord>(
            TenantResourceFileSql.FindOwned.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = owner, ResourceId = resourceB }).ConfigureAwait(false));
        var owned = await first.QuerySingleAsync<TenantResourceFileRecord>(
            TenantResourceFileSql.FindOwned.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = owner, ResourceId = resourceA }).ConfigureAwait(false);
        Assert.AreEqual(fileId, owned.Id);
        Assert.AreEqual(0, await first.ExecuteAsync(
            TenantResourceFileSql.MarkReady.Text,
            new { TenantId = tenantB, Id = fileId, OwnerModuleKey = owner, ResourceId = resourceA }).ConfigureAwait(false));
        Assert.AreEqual(1, await first.ExecuteAsync(
            TenantResourceFileSql.MarkReady.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = owner, ResourceId = resourceA }).ConfigureAwait(false));
        Assert.AreEqual(0, await first.ExecuteAsync(
            TenantResourceFileSql.Release.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = owner, ResourceId = resourceB }).ConfigureAwait(false));
        Assert.AreEqual("ready", await StatusAsync(first, fileId).ConfigureAwait(false));
        Assert.AreEqual(1, await first.ExecuteAsync(
            TenantResourceFileSql.Release.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = owner, ResourceId = resourceA }).ConfigureAwait(false));
        Assert.AreEqual("released", await StatusAsync(first, fileId).ConfigureAwait(false));
    }

    /// <summary>ListReady 只返回同一所属资源的 ready 文件，不得带出其他租户或资源。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task List_ready_is_scoped_to_owner_resource_async(DatabaseProvider provider)
    {
        var (connection, unused, tenantA, tenantB, resourceA, resourceB, fileA) =
            await SeedPendingPairAsync(provider).ConfigureAwait(false);
        await using var first = connection;
        await using var second = unused;
        var fileB = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        await InsertAsync(first, fileB, tenantB, "import_export", resourceB, now).ConfigureAwait(false);
        Assert.AreEqual(1, await first.ExecuteAsync(
            TenantResourceFileSql.MarkReady.Text,
            new { TenantId = tenantA, Id = fileA, OwnerModuleKey = "import_export", ResourceId = resourceA }).ConfigureAwait(false));
        Assert.AreEqual(1, await first.ExecuteAsync(
            TenantResourceFileSql.MarkReady.Text,
            new { TenantId = tenantB, Id = fileB, OwnerModuleKey = "import_export", ResourceId = resourceB }).ConfigureAwait(false));
        var ready = (await first.QueryAsync<TenantResourceFileReadyRecord>(
            TenantResourceFileSql.ListReady.Text,
            new { TenantId = tenantA, OwnerModuleKey = "import_export", ResourceId = resourceA }).ConfigureAwait(false))
            .Select(row => row.Id)
            .ToList();
        CollectionAssert.AreEquivalent(new[] { fileA }, ready);
    }

    /// <summary>陈旧 pending 只能由匹配所有权的语句提升或清除，跨租户不得删除元数据。</summary>
    /// <param name="provider">正式支持的数据库提供程序。</param>
    public static async Task Pending_promote_and_purge_stay_owner_bound_async(DatabaseProvider provider)
    {
        var (connection, unused, tenantA, tenantB, resourceA, _, fileId) =
            await SeedPendingPairAsync(provider).ConfigureAwait(false);
        await using var first = connection;
        await using var second = unused;
        Assert.AreEqual(0, await first.ExecuteAsync(
            TenantResourceFileSql.PurgePending.Text,
            new { TenantId = tenantB, Id = fileId, OwnerModuleKey = "import_export", ResourceId = resourceA }).ConfigureAwait(false));
        Assert.AreEqual("pending", await StatusAsync(first, fileId).ConfigureAwait(false));
        Assert.AreEqual(1, await first.ExecuteAsync(
            TenantResourceFileSql.PromotePending.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = "import_export", ResourceId = resourceA }).ConfigureAwait(false));
        Assert.AreEqual("ready", await StatusAsync(first, fileId).ConfigureAwait(false));
        Assert.AreEqual(0, await first.ExecuteAsync(
            TenantResourceFileSql.PurgePending.Text,
            new { TenantId = tenantA, Id = fileId, OwnerModuleKey = "import_export", ResourceId = resourceA }).ConfigureAwait(false));
    }

    /// <summary>写入一条租户 A 的 pending 文件，并预留租户 B 与另一资源标识。</summary>
    private static async Task<(DbConnection First, DbConnection Second, Guid TenantA, Guid TenantB, Guid ResourceA, Guid ResourceB, Guid FileId)> SeedPendingPairAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync().ConfigureAwait(false)
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync().ConfigureAwait(false);
        var runner = new DbUpMigrationRunner(Options.Create(new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }), NullLoggerFactory.Instance, MigrationContractOptionFactory.UuidOptions(), MigrationContractOptionFactory.NamingOptions());
        await runner.MigrateAsync().ConfigureAwait(false);
        var first = Connection(provider, connectionString);
        var second = Connection(provider, connectionString);
        await first.OpenAsync().ConfigureAwait(false);
        await second.OpenAsync().ConfigureAwait(false);
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var resourceA = Guid.CreateVersion7();
        var resourceB = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        await InsertAsync(first, fileId, tenantA, "import_export", resourceA, DateTime.UtcNow).ConfigureAwait(false);
        return (first, second, tenantA, tenantB, resourceA, resourceB, fileId);
    }

    /// <summary>按生产 Insert 语句写入 pending 租户资源文件。</summary>
    private static Task<int> InsertAsync(
        DbConnection connection,
        Guid id,
        Guid tenantId,
        string ownerModuleKey,
        Guid resourceId,
        DateTime createdAtUtc) =>
        connection.ExecuteAsync(
            TenantResourceFileSql.Insert.Text,
            new
            {
                Id = id,
                TenantId = tenantId,
                OwnerModuleKey = ownerModuleKey,
                ResourceId = resourceId,
                OriginalFileName = "report.xlsx",
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                SizeBytes = 1L,
                ContentHash = new string('a', 64),
                ProviderKey = "local",
                StorageKey = $"tenant/{id:N}",
                CreatedByUserId = Guid.CreateVersion7(),
                CreatedAtUtc = createdAtUtc,
            });

    /// <summary>读取当前状态键。</summary>
    private static Task<string> StatusAsync(DbConnection connection, Guid id) =>
        connection.QuerySingleAsync<string>("SELECT StatusKey FROM fn_files_tenant_resource_file WHERE Id=@Id", new { Id = id });

    /// <summary>创建标准 UUID 字节序的独立测试连接。</summary>
    private static DbConnection Connection(DatabaseProvider provider, string connectionString) => provider == DatabaseProvider.SqlServer
        ? new SqlConnection(connectionString)
        : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
}
