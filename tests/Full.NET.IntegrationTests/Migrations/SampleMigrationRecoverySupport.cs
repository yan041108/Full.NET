using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.EnterpriseRequest;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>按正式应用程序集和独立日志执行单个样例迁移，不能混入框架迁移清单。</summary>
internal static class SampleMigrationRecoverySupport
{
    internal static int Run(DatabaseProvider provider, string connectionString, string script)
    {
        var migration = provider == DatabaseProvider.SqlServer
            ? DeployChanges.To.SqlDatabase(connectionString)
            : DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(connectionString,
                MySqlGuidStorageMode.Binary16, allowUserVariables: true));
        var segment = provider == DatabaseProvider.SqlServer ? ".Migrations.SqlServer." : ".Migrations.MySql.";
        var result = migration.WithScriptsEmbeddedInAssembly(typeof(EnterpriseRequestModule).Assembly,
            name => name.Contains(segment, StringComparison.Ordinal) && name.EndsWith(script, StringComparison.Ordinal))
            .Build().PerformUpgrade();
        if (!result.Successful) throw new InvalidOperationException("Sample migration failed.", result.Error);
        return result.Scripts.Count();
    }
}
