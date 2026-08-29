using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
public sealed class MultiResultQueryTests
{
    [TestMethod]
    public async Task SqlServer_reads_ordered_results_and_reuses_the_scoped_connection()
    {
        await VerifyAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_reads_ordered_results_and_reuses_the_scoped_connection()
    {
        await VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyAsync(
        DatabaseProvider provider,
        string connectionString)
    {
        await using var services = BuildServices(provider, connectionString);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var executor = scope.ServiceProvider.GetRequiredService<IMultiResultQueryExecutor>();

        var result = await executor.QueryMultipleAsync(
            new SqlStatement(
                "test.multi_result",
                "SELECT 7 AS Value; SELECT 'fullnet' AS Name;",
                SqlDataScope.HostOnly),
            null,
            async (reader, cancellationToken) =>
            {
                var first = await reader.ReadSingleOrDefaultAsync<NumberRow>();
                var second = await reader.ReadAsync<NameRow>();
                return new AggregateResult(first!.Value, second.Single().Name);
            });

        Assert.AreEqual(new AggregateResult(7L, "fullnet"), result);

        var ordinary = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var value = await ordinary.QuerySingleOrDefaultAsync<int>(
            new SqlStatement(
                "test.after_multi_result",
                "SELECT 8;",
                SqlDataScope.HostOnly));
        Assert.AreEqual(8, value);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.QueryMultipleAsync(
                new SqlStatement(
                    "test.incomplete_multi_result",
                    "SELECT 1 AS Value; SELECT 2 AS Value;",
                    SqlDataScope.HostOnly),
                null,
                async (reader, cancellationToken) =>
                {
                    var first = await reader.ReadSingleOrDefaultAsync<NumberRow>();
                    return first!.Value;
                }));

        var afterFailure = await ordinary.QuerySingleOrDefaultAsync<int>(
            new SqlStatement(
                "test.after_incomplete_multi_result",
                "SELECT 9;",
                SqlDataScope.HostOnly));
        Assert.AreEqual(9, afterFailure);
    }

    private static ServiceProvider BuildServices(
        DatabaseProvider provider,
        string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(serviceProvider =>
            serviceProvider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(serviceProvider =>
            serviceProvider.GetRequiredService<CurrentTenantAccessor>());
        services.AddFullNetDapper(configuration, "Testing");
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            // 本用例只验证数据执行器；Outbox 的完整依赖由其专用集成测试负责。
            ValidateOnBuild = false,
            ValidateScopes = true,
        });
    }

    private sealed class NumberRow
    {
        public long Value { get; init; }
    }

    private sealed class NameRow
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed record AggregateResult(long Value, string Name);
}
