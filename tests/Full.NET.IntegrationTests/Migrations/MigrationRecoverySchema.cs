using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>
/// 标准全量 Runner 的恢复用例复用只读 schema，省去重复安装；破坏和目标迁移重放仍用原 Runner。
/// 首次安装、历史前缀、定制 Runner 不接此入口；已有对象的历史库仍执行完整迁移。
/// </summary>
internal static class MigrationRecoverySchema
{
    internal static async Task InitializeAsync(DbUpMigrationRunner runner, DatabaseProvider provider,
        string connectionString, Func<string, DbUpMigrationRunner> createTemplateRunner)
    {
        var hydrated = await ApiSchemaTemplate.TryHydrateEmptyDatabaseAsync(provider, connectionString,
            async (template, token) => { await createTemplateRunner(template).MigrateAsync(token); }, CancellationToken.None);
        if (!hydrated) await runner.MigrateAsync();
        // 测试结果记录实际使用路径，便于核对缓存命中，不能把回退全量安装报告为模板复用。
        Console.WriteLine($"schema-template-initialization provider={provider} cloned={hydrated}");
    }
}
