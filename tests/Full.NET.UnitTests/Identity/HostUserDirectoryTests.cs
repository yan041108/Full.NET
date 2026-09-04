using Full.NET.Data.Abstractions;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.HostUsers;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserDirectoryTests
{
    [TestMethod]
    public async Task Tenant_directory_uses_trusted_tenant_scope_for_page_and_batch_lookup()
    {
        var tenantId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf293");
        var firstUserId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");
        var secondUserId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf295");
        foreach (var provider in new[] { DatabaseProvider.SqlServer, DatabaseProvider.MySql })
        {
            var executor = new TenantSelectionQueryExecutor(
            [
                new HostUserDirectoryRecord(firstUserId, "tenant-user", "租户用户"),
                new HostUserDirectoryRecord(secondUserId, "host-member", "Host 成员"),
            ]);
            var currentTenant = Substitute.For<ICurrentTenant>();
            currentTenant.IsAvailable.Returns(true);
            currentTenant.IsHost.Returns(false);
            currentTenant.Id.Returns(tenantId);
            var directory = new TenantUserSelectionDirectory(
                executor,
                Options.Create(new DatabaseOptions { Provider = provider }),
                currentTenant);

            var page = await directory.ListActiveTenantUsersAsync(0, 999);
            var users = await directory.FindActiveTenantUsersAsync(
                [firstUserId, firstUserId, secondUserId]);

            Assert.AreEqual(1, page.Page);
            Assert.AreEqual(100, page.PageSize);
            Assert.AreEqual(2, page.Total);
            Assert.HasCount(2, users);
            Assert.AreEqual(
                provider == DatabaseProvider.SqlServer
                    ? "identity.list_active_tenant_user_selections.sql_server"
                    : "identity.list_active_tenant_user_selections.my_sql",
                executor.ListStatement?.Name);
            Assert.AreEqual(
                "identity.list_active_tenant_user_selections_by_ids",
                executor.BatchStatement?.Name);
            Assert.AreEqual(tenantId, ReadSqlParameter<Guid>(executor.ListParameters!, "TenantId"));
            Assert.AreEqual($"tenant:{tenantId:N}",
                ReadSqlParameter<string>(executor.ListParameters!, "TenantScopeKey"));
            CollectionAssert.AreEqual(
                new[] { firstUserId, secondUserId },
                ReadUserIds(executor.BatchParameters!));
        }
    }

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
            var activeUsers = await selectionDirectory.FindActiveHostUsersAsync(
                [firstUserId, firstUserId, secondUserId]);

            Assert.AreEqual(1, page.Page);
            Assert.AreEqual(100, page.PageSize);
            Assert.AreEqual(2, page.Total);
            Assert.HasCount(2, page.Items);
            Assert.HasCount(2, activeUsers);
            Assert.AreEqual(
                provider == DatabaseProvider.SqlServer
                    ? "identity.list_active_host_user_selections.sql_server"
                    : "identity.list_active_host_user_selections.my_sql",
                selectionExecutor.ListStatement?.Name);
            Assert.AreEqual(0, ReadIntParameter(selectionExecutor.Parameters!, "Offset"));
            Assert.AreEqual(100, ReadIntParameter(selectionExecutor.Parameters!, "PageSize"));
            Assert.AreEqual(
                "identity.list_active_host_user_selections_by_ids",
                selectionExecutor.BatchStatement?.Name);
            CollectionAssert.AreEqual(
                new[] { firstUserId, secondUserId },
                ReadUserIds(selectionExecutor.BatchParameters!));
        }
    }

    private static Guid[] ReadUserIds(object parameters) =>
        ReadSqlParameter<Guid[]>(parameters, "UserIds");

    private static int ReadIntParameter(object parameters, string propertyName) =>
        ReadSqlParameter<int>(parameters, propertyName);

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

        public SqlStatement? BatchStatement { get; private set; }

        public object? Parameters { get; private set; }

        public object? BatchParameters { get; private set; }

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
            if (statement.Name == "identity.list_active_host_user_selections_by_ids")
            {
                BatchStatement = statement;
                BatchParameters = parameters;
            }
            else
            {
                ListStatement = statement;
                Parameters = parameters;
            }
            return Task.FromResult<IReadOnlyList<T>>(records.Cast<T>().ToArray());
        }
    }

    private sealed class TenantSelectionQueryExecutor(
        IReadOnlyList<HostUserDirectoryRecord> records) : IQueryExecutor
    {
        public SqlStatement? ListStatement { get; private set; }

        public SqlStatement? BatchStatement { get; private set; }

        public object? ListParameters { get; private set; }

        public object? BatchParameters { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Assert.AreEqual("identity.count_active_tenant_user_selections", statement.Name);
            return Task.FromResult((T?)(object)(long)records.Count);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement.Name == "identity.list_active_tenant_user_selections_by_ids")
            {
                BatchStatement = statement;
                BatchParameters = parameters;
            }
            else
            {
                ListStatement = statement;
                ListParameters = parameters;
            }

            return Task.FromResult<IReadOnlyList<T>>(records.Cast<T>().ToArray());
        }
    }
}
