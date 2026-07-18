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
    public void Production_requires_explicit_storage_mode_configuration()
    {
        var configuration = CreateConfiguration(includeStorageMode: false);
        using var provider = new ServiceCollection()
            .AddFullNetDapper(configuration, Environments.Production)
            .BuildServiceProvider();

        var exception = Assert.ThrowsExactly<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.Contains(
            "MySqlGuidStorageMode",
            exception.Message,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Production_accepts_explicit_storage_mode_configuration()
    {
        var configuration = CreateConfiguration(includeStorageMode: true);
        using var provider = new ServiceCollection()
            .AddFullNetDapper(configuration, Environments.Production)
            .BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        Assert.AreEqual(MySqlGuidStorageMode.LegacyChar36, options.MySqlGuidStorageMode);
    }

    private static IConfiguration CreateConfiguration(bool includeStorageMode)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = DatabaseProvider.MySql.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = ConnectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
        };
        if (includeStorageMode)
        {
            values[$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                MySqlGuidStorageMode.LegacyChar36.ToString();
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
