using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.HostUsers;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserDirectoryTests
{
    [TestMethod]
    public async Task Cross_module_directories_batch_display_and_page_active_host_users()
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

        foreach (var provider in new[] { DatabaseProvider.SqlServer, DatabaseProvider.MySql })
        {
            var selectionExecutor = new SelectionQueryExecutor(
                [
                    new HostUserDirectoryRecord(firstUserId, "admin", "系统管理员"),
                    new HostUserDirectoryRecord(secondUserId, "auditor", "审计员"),
                ]);
            var selectionDirectory = new HostUserSelectionDirectory(
                selectionExecutor,
                Options.Create(new DatabaseOptions { Provider = provider }));

            var page = await selectionDirectory.ListActiveHostUsersAsync(0, 999);

            Assert.AreEqual(1, page.Page);
            Assert.AreEqual(100, page.PageSize);
            Assert.AreEqual(2, page.Total);
            Assert.HasCount(2, page.Items);
            Assert.AreEqual(
                provider == DatabaseProvider.SqlServer
                    ? "identity.list_active_host_user_selections.sql_server"
                    : "identity.list_active_host_user_selections.my_sql",
                selectionExecutor.ListStatement?.Name);
            Assert.AreEqual(0, ReadIntParameter(selectionExecutor.Parameters!, "Offset"));
            Assert.AreEqual(100, ReadIntParameter(selectionExecutor.Parameters!, "PageSize"));
        }
    }

    private static Guid[] ReadUserIds(object parameters) =>
        (Guid[])parameters.GetType().GetProperty("UserIds")!.GetValue(parameters)!;

    private static int ReadIntParameter(object parameters, string propertyName) =>
        (int)parameters.GetType().GetProperty(propertyName)!.GetValue(parameters)!;

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

    private sealed class SelectionQueryExecutor(
        IReadOnlyList<HostUserDirectoryRecord> records) : IQueryExecutor
    {
        public SqlStatement? ListStatement { get; private set; }

        public object? Parameters { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual(
                "identity.count_active_host_user_selections",
                statement.Name);
            return Task.FromResult((T?)(object)(long)records.Count);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            ListStatement = statement;
            Parameters = parameters;
            return Task.FromResult<IReadOnlyList<T>>(records.Cast<T>().ToArray());
        }
    }
}
