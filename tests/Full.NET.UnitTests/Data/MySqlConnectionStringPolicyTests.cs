using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class MySqlConnectionStringPolicyTests
{
    private const string ConnectionString =
        "Server=localhost;Database=fullnet;User ID=fullnet;Password=unit-test-secret";

    [TestMethod]
    public void Storage_mode_contains_only_legacy_and_binary_values()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                MySqlGuidStorageMode.LegacyChar36,
                MySqlGuidStorageMode.Binary16,
            },
            Enum.GetValues<MySqlGuidStorageMode>());
        Assert.AreEqual(
            MySqlGuidStorageMode.LegacyChar36,
            new DatabaseOptions().MySqlGuidStorageMode);
    }

    [TestMethod]
    public void Legacy_mode_keeps_driver_default_and_disables_user_variables()
    {
        var actual = MySqlConnectionStringPolicy.Create(
            ConnectionString,
            MySqlGuidStorageMode.LegacyChar36,
            allowUserVariables: false);
        var builder = new MySqlConnectionStringBuilder(actual);

        Assert.AreEqual(MySqlGuidFormat.Default, builder.GuidFormat);
        Assert.IsFalse(builder.ContainsKey("GuidFormat"));
        Assert.IsFalse(builder.AllowUserVariables);
    }

    [TestMethod]
    public void Binary_mode_outputs_binary16_and_disables_user_variables()
    {
        var actual = MySqlConnectionStringPolicy.Create(
            ConnectionString,
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false);
        var builder = new MySqlConnectionStringBuilder(actual);

        Assert.AreEqual(MySqlGuidFormat.Binary16, builder.GuidFormat);
        Assert.IsTrue(builder.ContainsKey("GuidFormat"));
        Assert.IsFalse(builder.AllowUserVariables);
    }

    [TestMethod]
    public void Migration_mode_only_adds_allow_user_variables()
    {
        var actual = MySqlConnectionStringPolicy.Create(
            ConnectionString,
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: true);
        var builder = new MySqlConnectionStringBuilder(actual);

        Assert.AreEqual(MySqlGuidFormat.Binary16, builder.GuidFormat);
        Assert.IsTrue(builder.AllowUserVariables);
    }

    [TestMethod]
    public void Explicit_char36_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            MySqlConnectionStringPolicy.Create(
                $"{ConnectionString};GuidFormat=Char36",
                MySqlGuidStorageMode.LegacyChar36,
                allowUserVariables: false));
    }

    [TestMethod]
    public void Time_swap_binary16_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            MySqlConnectionStringPolicy.Create(
                $"{ConnectionString};GuidFormat=TimeSwapBinary16",
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
    }

    [TestMethod]
    public void Conflicting_guid_format_is_rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            MySqlConnectionStringPolicy.Create(
                $"{ConnectionString};GuidFormat=Binary16",
                MySqlGuidStorageMode.LegacyChar36,
                allowUserVariables: false));
    }

    [TestMethod]
    public void Rejection_does_not_echo_connection_string_or_secret()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            MySqlConnectionStringPolicy.Create(
                $"{ConnectionString};GuidFormat=Char36",
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));

        Assert.DoesNotContain("unit-test-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ConnectionString, exception.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void Explicit_old_guids_is_rejected_for_every_storage_mode()
    {
        foreach (var mode in Enum.GetValues<MySqlGuidStorageMode>())
        {
            foreach (var oldGuids in new[] { false, true })
            {
                var exception = Assert.ThrowsExactly<ArgumentException>(() =>
                    MySqlConnectionStringPolicy.Create(
                        $"{ConnectionString};Old Guids={oldGuids}",
                        mode,
                        allowUserVariables: false));

                Assert.DoesNotContain(
                    "unit-test-secret",
                    exception.Message,
                    StringComparison.Ordinal);
            }
        }
    }

    [TestMethod]
    public void Production_rejects_invalid_database_configuration()
    {
        var configuration = CreateConfiguration(includeStorageMode: false);
        using var provider = new ServiceCollection()
            .AddFullNetDapper(configuration, Environments.Production)
            .BuildServiceProvider();

        var missingStorageModeException =
            Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.Contains(
            "MySqlGuidStorageMode",
            missingStorageModeException.Message,
            StringComparison.Ordinal);

        var invalidProviderConfiguration = CreateConfiguration(
            includeStorageMode: true,
            storageMode: MySqlGuidStorageMode.Binary16,
            provider: (DatabaseProvider)int.MaxValue);
        using var invalidProvider = new ServiceCollection()
            .AddFullNetDapper(
                invalidProviderConfiguration,
                Environments.Production)
            .BuildServiceProvider();

        var invalidProviderException =
            Assert.ThrowsExactly<OptionsValidationException>(() =>
                _ = invalidProvider
                    .GetRequiredService<IOptions<DatabaseOptions>>()
                    .Value);

        Assert.Contains(
            "Provider",
            invalidProviderException.Message,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Production_accepts_explicit_binary_storage_mode_configuration()
    {
        var configuration = CreateConfiguration(
            includeStorageMode: true,
            storageMode: MySqlGuidStorageMode.Binary16);
        using var provider = new ServiceCollection()
            .AddFullNetDapper(configuration, Environments.Production)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.AreEqual(MySqlGuidStorageMode.Binary16, options.MySqlGuidStorageMode);
    }

    [TestMethod]
    public void Two_parameter_overload_infers_non_production_environment()
    {
        var configuration = CreateConfiguration(
            includeStorageMode: false,
            environmentName: Environments.Development);
        using var provider = new ServiceCollection()
            .AddFullNetDapper(configuration)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.AreEqual(MySqlGuidStorageMode.LegacyChar36, options.MySqlGuidStorageMode);
    }

    [TestMethod]
    public void Two_parameter_overload_enforces_production_explicit_mode()
    {
        var configuration = CreateConfiguration(
            includeStorageMode: false,
            environmentName: Environments.Production);
        using var provider = new ServiceCollection()
            .AddFullNetDapper(configuration)
            .BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    [TestMethod]
    public void Two_parameter_overload_treats_missing_environment_as_production()
    {
        var configuration = CreateConfiguration(includeStorageMode: false);
        using var provider = new ServiceCollection()
            .AddFullNetDapper(configuration)
            .BuildServiceProvider();

        Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    private static IConfiguration CreateConfiguration(
        bool includeStorageMode,
        string? environmentName = null,
        MySqlGuidStorageMode storageMode = MySqlGuidStorageMode.LegacyChar36,
        DatabaseProvider provider = DatabaseProvider.MySql)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = ConnectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
        };
        if (includeStorageMode)
        {
            values[$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                storageMode.ToString();
        }

        if (environmentName is not null)
        {
            values[HostDefaults.EnvironmentKey] = environmentName;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
