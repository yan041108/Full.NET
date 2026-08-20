using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Features.ManageHostConfigEntries;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.UnitTests.Settings;

[TestClass]
public sealed class SettingsSecretValueResolverTests
{
    [TestMethod]
    public async Task ResolveSecretValueAsync_ReturnsPlaintext_ForActiveSecret()
    {
        var query = new StubQueryExecutor(
            new ConfigEntrySecretRecord
            {
                ValueKind = ConfigValueKinds.Secret,
                Value = "bearer-token",
                IsActive = true,
            });
        var resolver = new SettingsSecretValueResolver(query);

        var result = await resolver.ResolveSecretValueAsync(
            "jobs.http.secrets.demo",
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("bearer-token", result.Value);
    }

    [TestMethod]
    public async Task ResolveSecretValueAsync_RejectsMissingOrNonSecret()
    {
        var resolver = new SettingsSecretValueResolver(new StubQueryExecutor(null));

        var missing = await resolver.ResolveSecretValueAsync(
            "missing.key",
            CancellationToken.None);
        Assert.IsFalse(missing.IsSuccess);
        Assert.AreEqual(
            SettingsErrorCodes.ConfigEntrySecretUnavailable,
            missing.Error?.Code);
    }

    private sealed class StubQueryExecutor(ConfigEntrySecretRecord? record) : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(ConfigEntrySecretRecord))
            {
                return Task.FromResult((T?)(object?)record);
            }

            throw new InvalidOperationException($"Unexpected query {statement.Name}.");
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
