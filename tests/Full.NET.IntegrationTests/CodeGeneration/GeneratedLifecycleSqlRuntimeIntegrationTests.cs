using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.CodeGeneration;

[TestClass]
public sealed class GeneratedLifecycleSqlRuntimeIntegrationTests
{
    [TestMethod]
    public async Task SqlServer_generated_soft_delete_sql_executes_lifecycle_matrix()
    {
        await LifecycleRuntimeSqlTestSupport.AssertSoftDeleteLifecycleMatrixAsync(
            SharedDatabaseFixture.CreateSqlServerDatabaseAsync,
            static connectionString => new SqlConnection(connectionString),
            sqlServer: true);
    }

    [TestMethod]
    public async Task MySql_generated_soft_delete_sql_executes_lifecycle_matrix()
    {
        await LifecycleRuntimeSqlTestSupport.AssertSoftDeleteLifecycleMatrixAsync(
            SharedDatabaseFixture.CreateMySqlDatabaseAsync,
            static connectionString => new MySqlConnection(connectionString),
            sqlServer: false);
    }

    [TestMethod]
    public async Task SqlServer_generated_hard_delete_sql_executes_lifecycle_matrix()
    {
        await LifecycleRuntimeSqlTestSupport.AssertHardDeleteLifecycleMatrixAsync(
            SharedDatabaseFixture.CreateSqlServerDatabaseAsync,
            static connectionString => new SqlConnection(connectionString),
            sqlServer: true);
    }

    [TestMethod]
    public async Task MySql_generated_hard_delete_sql_executes_lifecycle_matrix()
    {
        await LifecycleRuntimeSqlTestSupport.AssertHardDeleteLifecycleMatrixAsync(
            SharedDatabaseFixture.CreateMySqlDatabaseAsync,
            static connectionString => new MySqlConnection(connectionString),
            sqlServer: false);
    }
}