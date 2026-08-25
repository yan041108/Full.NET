using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.Data.Dapper;

internal static class DatabasePoolConfiguration
{
    internal static int ReadMaximumPoolSize(DatabaseOptions options) =>
        options.Provider switch
        {
            DatabaseProvider.SqlServer =>
                new SqlConnectionStringBuilder(options.ConnectionString).MaxPoolSize,
            DatabaseProvider.MySql => checked((int)new MySqlConnectionStringBuilder(
                options.ConnectionString).MaximumPoolSize),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Provider,
                "Unsupported database provider."),
        };

    internal static bool IsPoolingEnabled(DatabaseOptions options) =>
        options.Provider switch
        {
            DatabaseProvider.SqlServer =>
                new SqlConnectionStringBuilder(options.ConnectionString).Pooling,
            DatabaseProvider.MySql =>
                new MySqlConnectionStringBuilder(options.ConnectionString).Pooling,
            _ => false,
        };
}

internal sealed class DatabaseCapacityOptionsValidator(
    IOptions<DatabaseOptions> databaseOptions)
    : IValidateOptions<DatabaseCapacityOptions>
{
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;

    public ValidateOptionsResult Validate(
        string? name,
        DatabaseCapacityOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        if (options.HostRole == DatabaseHostRole.Unspecified
            || !Enum.IsDefined(options.HostRole))
        {
            failures.Add("DatabaseCapacity:HostRole must be api, worker, or migrator.");
        }

        AddRangeFailure(failures, options.PermitLimit < 1,
            "DatabaseCapacity:PermitLimit must be greater than zero.");
        AddRangeFailure(failures, options.QueueLimit is < 0 or > 1000,
            "DatabaseCapacity:QueueLimit must be between 0 and 1000.");
        AddRangeFailure(failures, options.AcquireTimeoutMilliseconds is < 1 or > 60000,
            "DatabaseCapacity:AcquireTimeoutMilliseconds must be between 1 and 60000.");
        AddRangeFailure(failures, options.ExpectedMaxPoolSize < 1,
            "DatabaseCapacity:ExpectedMaxPoolSize must be greater than zero.");
        AddRangeFailure(
            failures,
            options.HealthReserve < 0 || options.CriticalWorkerReserve < 0,
            "DatabaseCapacity connection reserves must not be negative.");
        AddRangeFailure(
            failures,
            options.ApiMaxReplicas < 1
            || options.ApiMaxPoolSize < 1
            || options.WorkerMaxReplicas < 1
            || options.WorkerMaxPoolSize < 1
            || options.MigrationReserve < 0
            || options.TotalBudget < 1,
            "DatabaseCapacity cluster budget values must be positive and MigrationReserve must not be negative.");

        if (!DatabasePoolConfiguration.IsPoolingEnabled(_databaseOptions))
        {
            failures.Add("DatabaseCapacity requires provider connection pooling to be enabled.");
        }

        var actualMaxPoolSize = DatabasePoolConfiguration.ReadMaximumPoolSize(
            _databaseOptions);
        if (options.ExpectedMaxPoolSize != actualMaxPoolSize)
        {
            failures.Add(
                "DatabaseCapacity:ExpectedMaxPoolSize must equal the provider connection string "
                + $"maximum pool size ({actualMaxPoolSize}).");
        }

        var rolePoolSize = options.HostRole switch
        {
            DatabaseHostRole.Api => options.ApiMaxPoolSize,
            DatabaseHostRole.Worker => options.WorkerMaxPoolSize,
            DatabaseHostRole.Migrator => options.ExpectedMaxPoolSize,
            _ => options.ExpectedMaxPoolSize,
        };
        if (options.ExpectedMaxPoolSize != rolePoolSize)
        {
            failures.Add(
                "DatabaseCapacity:ExpectedMaxPoolSize must equal the declared pool size for HostRole.");
        }

        var reservedPermits = (long)options.PermitLimit
            + options.HealthReserve
            + options.CriticalWorkerReserve;
        if (reservedPermits > options.ExpectedMaxPoolSize)
        {
            failures.Add(
                "DatabaseCapacity:PermitLimit + HealthReserve + CriticalWorkerReserve "
                + "must not exceed ExpectedMaxPoolSize.");
        }

        var requiredBudget = checked(
            (long)options.ApiMaxReplicas * options.ApiMaxPoolSize
            + (long)options.WorkerMaxReplicas * options.WorkerMaxPoolSize
            + options.MigrationReserve);
        if (requiredBudget > options.TotalBudget)
        {
            failures.Add(
                $"DatabaseCapacity cluster connection budget requires {requiredBudget} "
                + $"connections but TotalBudget is {options.TotalBudget}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRangeFailure(
        ICollection<string> failures,
        bool invalid,
        string message)
    {
        if (invalid)
        {
            failures.Add(message);
        }
    }
}
