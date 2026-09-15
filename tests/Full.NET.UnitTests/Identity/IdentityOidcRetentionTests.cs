using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Retention;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityOidcRetentionTests
{
    [TestMethod]
    public void Retention_registration_uses_disabled_defaults_and_rejects_unsafe_bounds()
    {
        using var defaults = CreateProvider(
            new Dictionary<string, string?>());
        var options = defaults.GetRequiredService<
            IOptions<IdentityOidcRetentionOptions>>().Value;

        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(30, options.RetentionDays);
        Assert.AreEqual(3600, options.PollSeconds);

        using var invalid = CreateProvider(
            new Dictionary<string, string?>
            {
                ["Identity:Oidc:Retention:RetentionDays"] = "0",
                ["Identity:Oidc:Retention:PollSeconds"] = "59",
            });
        var exception = Assert.ThrowsExactly<OptionsValidationException>(
            invalid.GetRequiredService<IStartupValidator>().Validate);

        Assert.AreEqual(2, exception.Failures.Count());
    }

    [TestMethod]
    public async Task Disabled_retention_does_not_touch_the_database()
    {
        var command = new RecordingCommandExecutor();
        var runner = CreateRunner(
            command,
            new IdentityOidcOptions { Enable = true });

        var result = await runner.RunOnceAsync(
            new IdentityOidcRetentionOptions { Enabled = false },
            CancellationToken.None);

        Assert.AreEqual(0, result.TotalDeleted);
        Assert.AreEqual(0, command.Statements.Count);
    }

    [TestMethod]
    public async Task Disabled_oidc_does_not_touch_the_database()
    {
        var command = new RecordingCommandExecutor();
        var runner = CreateRunner(
            command,
            new IdentityOidcOptions { Enable = false });

        var result = await runner.RunOnceAsync(
            new IdentityOidcRetentionOptions { Enabled = true, RetentionDays = 30 },
            CancellationToken.None);

        Assert.AreEqual(0, result.TotalDeleted);
        Assert.AreEqual(0, command.Statements.Count);
    }

    [TestMethod]
    public async Task Enabled_retention_prunes_tokens_before_authorizations()
    {
        var command = new RecordingCommandExecutor(
            new Dictionary<string, Queue<int>>(StringComparer.Ordinal)
            {
                ["identity.prune_oidc_tokens"] = new Queue<int>([2]),
                ["identity.prune_oidc_authorizations"] = new Queue<int>([1]),
            });
        var runner = CreateRunner(
            command,
            new IdentityOidcOptions { Enable = true });

        var result = await runner.RunOnceAsync(
            new IdentityOidcRetentionOptions
            {
                Enabled = true,
                RetentionDays = 30,
            },
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "identity.prune_oidc_tokens",
                "identity.prune_oidc_authorizations",
            },
            command.Statements.Select(statement => statement.Name).ToArray());
        Assert.AreEqual(2, result.TokensDeleted);
        Assert.AreEqual(1, result.AuthorizationsDeleted);
        Assert.AreEqual(
            new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero),
            ReadThreshold(command.Parameters[0]));
        Assert.AreEqual(
            new DateTimeOffset(2026, 6, 29, 0, 0, 0, TimeSpan.Zero),
            ReadThreshold(command.Parameters[1]));
    }

    private static ServiceProvider CreateProvider(
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddIdentityOidcRetention(configuration);
        return services.BuildServiceProvider();
    }

    private static IdentityOidcRetentionRunner CreateRunner(
        ICommandExecutor command,
        IdentityOidcOptions oidcOptions) =>
        new(
            command,
            new FixedClock(),
            Options.Create(oidcOptions));

    private static DateTimeOffset ReadThreshold(object parameters) =>
        ReadSqlParameter<DateTimeOffset>(parameters, "Threshold");

    private sealed class RecordingCommandExecutor(
        IReadOnlyDictionary<string, Queue<int>>? results = null)
        : ICommandExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public List<object> Parameters { get; } = [];

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            Parameters.Add(parameters
                ?? throw new InvalidOperationException("Parameters are required."));
            var affectedRows = results is null
                ? 0
                : results[statement.Name].Dequeue();
            return Task.FromResult(affectedRows);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
    }
}