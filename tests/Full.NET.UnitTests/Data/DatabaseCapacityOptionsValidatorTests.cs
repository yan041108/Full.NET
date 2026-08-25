using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class DatabaseCapacityOptionsValidatorTests
{
    [TestMethod]
    public void ReadMaximumPoolSize_UsesSqlServerConfiguredAndDefaultValues()
    {
        Assert.AreEqual(
            37,
            DatabasePoolConfiguration.ReadMaximumPoolSize(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=localhost;Database=fullnet;Max Pool Size=37",
            }));
        Assert.AreEqual(
            100,
            DatabasePoolConfiguration.ReadMaximumPoolSize(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=localhost;Database=fullnet",
            }));
    }

    [TestMethod]
    public void ReadMaximumPoolSize_UsesMySqlConfiguredAndDefaultValues()
    {
        Assert.AreEqual(
            23,
            DatabasePoolConfiguration.ReadMaximumPoolSize(new DatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
                ConnectionString = "Server=localhost;Database=fullnet;Maximum Pool Size=23",
            }));
        Assert.AreEqual(
            100,
            DatabasePoolConfiguration.ReadMaximumPoolSize(new DatabaseOptions
            {
                Provider = DatabaseProvider.MySql,
                ConnectionString = "Server=localhost;Database=fullnet",
            }));
    }

    [TestMethod]
    public void Validate_AllowsDisabledCapacityProtection()
    {
        var validator = CreateValidator(new DatabaseOptions());

        var result = validator.Validate(
            Options.DefaultName,
            new DatabaseCapacityOptions { Enabled = false });

        Assert.IsTrue(result.Succeeded);
    }

    [TestMethod]
    public void Validate_AcceptsConsistentApiBudget()
    {
        var validator = CreateValidator(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "Server=localhost;Database=fullnet;Max Pool Size=40",
        });

        var result = validator.Validate(
            Options.DefaultName,
            CreateValidOptions());

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Failures ?? []));
    }

    [TestMethod]
    public void Validate_RejectsActualPoolAndRoleDeclarationMismatch()
    {
        var validator = CreateValidator(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "Server=localhost;Database=fullnet;Max Pool Size=41",
        });

        var result = validator.Validate(
            Options.DefaultName,
            CreateValidOptions());

        CollectionAssert.Contains(
            (result.Failures ?? []).ToArray(),
            "DatabaseCapacity:ExpectedMaxPoolSize must equal the provider connection string maximum pool size (41)."
        );
    }

    [TestMethod]
    public void Validate_RejectsPerProcessReserveOverflow()
    {
        var options = CreateValidOptions();
        options.PermitLimit = 38;
        options.HealthReserve = 2;
        options.CriticalWorkerReserve = 1;
        var validator = CreateValidator(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "Server=localhost;Database=fullnet;Max Pool Size=40",
        });

        var result = validator.Validate(Options.DefaultName, options);

        CollectionAssert.Contains(
            (result.Failures ?? []).ToArray(),
            "DatabaseCapacity:PermitLimit + HealthReserve + CriticalWorkerReserve must not exceed ExpectedMaxPoolSize."
        );
    }

    [TestMethod]
    public void Validate_RejectsClusterBudgetOverflow()
    {
        var options = CreateValidOptions();
        options.TotalBudget = 579;
        var validator = CreateValidator(new DatabaseOptions
        {
            Provider = DatabaseProvider.SqlServer,
            ConnectionString = "Server=localhost;Database=fullnet;Max Pool Size=40",
        });

        var result = validator.Validate(Options.DefaultName, options);

        CollectionAssert.Contains(
            (result.Failures ?? []).ToArray(),
            "DatabaseCapacity cluster connection budget requires 580 connections but TotalBudget is 579."
        );
    }

    [TestMethod]
    public void AddFullNetDapper_BindsCapacityOptionsAndRegistersAdmissionGate()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer",
                ["Database:ConnectionString"] =
                    "Server=localhost;Database=fullnet;Max Pool Size=40",
                ["DatabaseCapacity:Enabled"] = "true",
                ["DatabaseCapacity:HostRole"] = "Api",
                ["DatabaseCapacity:PermitLimit"] = "37",
                ["DatabaseCapacity:QueueLimit"] = "1",
                ["DatabaseCapacity:AcquireTimeoutMilliseconds"] = "250",
                ["DatabaseCapacity:ExpectedMaxPoolSize"] = "40",
                ["DatabaseCapacity:HealthReserve"] = "2",
                ["DatabaseCapacity:CriticalWorkerReserve"] = "1",
                ["DatabaseCapacity:ApiMaxReplicas"] = "12",
                ["DatabaseCapacity:ApiMaxPoolSize"] = "40",
                ["DatabaseCapacity:WorkerMaxReplicas"] = "8",
                ["DatabaseCapacity:WorkerMaxPoolSize"] = "10",
                ["DatabaseCapacity:MigrationReserve"] = "20",
                ["DatabaseCapacity:TotalBudget"] = "600",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddFullNetDapper(configuration, Environments.Development);
        using var provider = services.BuildServiceProvider();

        Assert.IsTrue(provider
            .GetRequiredService<IOptions<DatabaseCapacityOptions>>()
            .Value.Enabled);
        Assert.IsNotNull(provider.GetRequiredService<DatabaseAdmissionGate>());
        Assert.IsNotNull(provider.GetRequiredService<IDbConnectionFactory>());
    }

    private static DatabaseCapacityOptionsValidator CreateValidator(
        DatabaseOptions databaseOptions) => new(Options.Create(databaseOptions));

    private static DatabaseCapacityOptions CreateValidOptions() => new()
    {
        Enabled = true,
        HostRole = DatabaseHostRole.Api,
        PermitLimit = 37,
        QueueLimit = 1,
        AcquireTimeoutMilliseconds = 250,
        ExpectedMaxPoolSize = 40,
        HealthReserve = 2,
        CriticalWorkerReserve = 1,
        ApiMaxReplicas = 12,
        ApiMaxPoolSize = 40,
        WorkerMaxReplicas = 8,
        WorkerMaxPoolSize = 10,
        MigrationReserve = 20,
        TotalBudget = 600,
    };
}
