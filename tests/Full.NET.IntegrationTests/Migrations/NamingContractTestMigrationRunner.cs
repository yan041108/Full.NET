using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

internal static class NamingContractTestMigrationRunner
{
    public static async Task PrepareMySqlExpandStateAsync(string connectionString)
    {
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough009Async(connectionString);
        await using var connection = new MySqlConnector.MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await connection.OpenAsync();
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);
        await NamingExpandTestMigrationRunner.MigrateMySqlThrough010Async(connectionString);
    }

    public static async Task PrepareSqlServerExpandStateAsync(string connectionString)
    {
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough009Async(connectionString);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await NamingExpandTestData.InsertTenantAndOutboxAsync(connection);
        await NamingExpandTestMigrationRunner.MigrateSqlServerThrough010Async(connectionString);
    }

    public static DbUpMigrationRunner CreateMySqlRunner(string connectionString) => new(
        Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }),
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        MigrationContractOptionFactory.UuidOptions(),
        MigrationContractOptionFactory.NamingOptions());

    public static DbUpMigrationRunner CreateSqlServerRunner(string connectionString) => new(
        Microsoft.Extensions.Options.Options.Create(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = connectionString,
            CommandTimeoutSeconds = 300,
        }),
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        MigrationContractOptionFactory.UuidOptions(),
        MigrationContractOptionFactory.NamingOptions());
}
