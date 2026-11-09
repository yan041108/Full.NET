using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Data;

/// <summary>为消费者复现提供可分别占用的普通、关键、健康及授权许可。</summary>
internal static class WorkerAdmissionTestConfiguration
{
    internal static string WithSmallPool(DatabaseProvider provider, string connectionString) =>
        provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(connectionString) { MaxPoolSize = 4 }.ConnectionString
            : new MySqlConnectionStringBuilder(connectionString) { MaximumPoolSize = 4 }.ConnectionString;

    internal static Dictionary<string, string?> CreateSettings() => new()
    {
        ["DatabaseCapacity:Enabled"] = "true",
        ["DatabaseCapacity:HostRole"] = "Worker",
        ["DatabaseCapacity:PermitLimit"] = "1",
        ["DatabaseCapacity:QueueLimit"] = "1",
        ["DatabaseCapacity:AcquireTimeoutMilliseconds"] = "1500",
        ["DatabaseCapacity:ExpectedMaxPoolSize"] = "4",
        ["DatabaseCapacity:HealthReserve"] = "1",
        ["DatabaseCapacity:CriticalWorkerReserve"] = "1",
        ["DatabaseCapacity:AuthorizationReadReserve"] = "1",
        ["DatabaseCapacity:ApiMaxReplicas"] = "1",
        ["DatabaseCapacity:ApiMaxPoolSize"] = "4",
        ["DatabaseCapacity:WorkerMaxReplicas"] = "2",
        ["DatabaseCapacity:WorkerMaxPoolSize"] = "4",
        ["DatabaseCapacity:TotalBudget"] = "12"
    };
}
