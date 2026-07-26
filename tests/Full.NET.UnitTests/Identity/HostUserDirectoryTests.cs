using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.HostUsers;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserDirectoryTests
{
    [TestMethod]
    public async Task Batch_lookup_deduplicates_ids_and_returns_existing_host_users()
    {
        var firstUserId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");
        var secondUserId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf295");
        var queryExecutor = new RecordingQueryExecutor(
            [
                new HostUserDirectoryRecord(firstUserId, "admin", "系统管理员"),
                new HostUserDirectoryRecord(secondUserId, "auditor", "审计员"),
            ]);
        var directory = new HostUserDirectory(queryExecutor);

        var users = await directory.FindHostUsersAsync(
            [firstUserId, firstUserId, secondUserId]);

        Assert.HasCount(2, users);
        Assert.AreEqual("系统管理员", users[firstUserId].DisplayName);
        Assert.AreEqual("auditor", users[secondUserId].Username);
        Assert.AreEqual(1, queryExecutor.QueryCount);
        Assert.AreEqual(
            "identity.list_host_users_by_ids",
            queryExecutor.Statement?.Name);
        CollectionAssert.AreEqual(
            new[] { firstUserId, secondUserId },
            ReadUserIds(queryExecutor.Parameters!));
    }

    private static Guid[] ReadUserIds(object parameters) =>
        (Guid[])parameters.GetType().GetProperty("UserIds")!.GetValue(parameters)!;

    private sealed class RecordingQueryExecutor(
        IReadOnlyList<HostUserDirectoryRecord> records) : IQueryExecutor
    {
        public int QueryCount { get; private set; }

        public SqlStatement? Statement { get; private set; }

        public object? Parameters { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            QueryCount++;
            Statement = statement;
            Parameters = parameters;
            return Task.FromResult<IReadOnlyList<T>>(records.Cast<T>().ToArray());
        }
    }
}
