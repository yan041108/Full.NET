using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Organization;

[TestClass]
public sealed class OrganizationHostUserDirectoryCompositionTests
{
    private static readonly Guid UserA = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf291");
    private static readonly Guid UserB = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf292");
    private static readonly Guid UnitId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf293");
    private static readonly Guid CurrentUserId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf294");

    [TestMethod]
    public async Task UserUnit_ListAsync_batches_distinct_host_users_once_per_page()
    {
        var directory = new RecordingHostUserDisplayDirectory(
            new HostUserDirectoryEntry(UserA, "user-a", "User A"),
            new HostUserDirectoryEntry(UserB, "user-b", "User B"));
        var query = CreateUserUnitQueryExecutor(
            total: 3,
            rows:
            [
                CreateUserUnitRow(UserA),
                CreateUserUnitRow(UserA),
                CreateUserUnitRow(UserB),
            ]);
        var service = CreateUserUnitQueryService(query, directory);

        var result = await service.ListAsync(
            CurrentUserId,
            isSuperAdministrator: true,
            page: 1,
            pageSize: 10,
            userId: null,
            unitId: null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(3, result.Value!.Items);
        Assert.HasCount(1, directory.Calls);
        CollectionAssert.AreEquivalent(
            new[] { UserA, UserB },
            directory.Calls[0].UserIds);
    }

    [TestMethod]
    public async Task UserPosition_ListAsync_batches_distinct_host_users_once_per_page()
    {
        var directory = new RecordingHostUserDisplayDirectory(
            new HostUserDirectoryEntry(UserA, "user-a", "User A"),
            new HostUserDirectoryEntry(UserB, "user-b", "User B"));
        var query = CreateUserPositionQueryExecutor(
            total: 2,
            rows:
            [
                CreateUserPositionRow(UserA),
                CreateUserPositionRow(UserB),
                CreateUserPositionRow(UserA),
            ]);
        var service = CreateUserPositionQueryService(query, directory);

        var result = await service.ListAsync(
            page: 1,
            pageSize: 10,
            userId: null,
            positionId: null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(3, result.Value!.Items);
        Assert.HasCount(1, directory.Calls);
        CollectionAssert.AreEquivalent(
            new[] { UserA, UserB },
            directory.Calls[0].UserIds);
    }

    [TestMethod]
    public async Task UserUnit_ListAsync_with_empty_page_does_not_query_host_directory()
    {
        var directory = new RecordingHostUserDisplayDirectory();
        var query = CreateUserUnitQueryExecutor(total: 0, rows: []);
        var service = CreateUserUnitQueryService(query, directory);

        var result = await service.ListAsync(
            CurrentUserId,
            isSuperAdministrator: true,
            page: 1,
            pageSize: 10,
            userId: null,
            unitId: null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsEmpty(result.Value!.Items);
        Assert.HasCount(1, directory.Calls);
        Assert.IsEmpty(directory.Calls[0].UserIds);
    }

    [TestMethod]
    public async Task UserUnit_ListAsync_filters_rows_when_host_user_is_missing()
    {
        var directory = new RecordingHostUserDisplayDirectory(
            new HostUserDirectoryEntry(UserA, "user-a", "User A"));
        var query = CreateUserUnitQueryExecutor(
            total: 2,
            rows:
            [
                CreateUserUnitRow(UserA),
                CreateUserUnitRow(UserB),
            ]);
        var service = CreateUserUnitQueryService(query, directory);

        var result = await service.ListAsync(
            CurrentUserId,
            isSuperAdministrator: true,
            page: 1,
            pageSize: 10,
            userId: null,
            unitId: null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, result.Value!.Items);
        Assert.AreEqual(UserA, result.Value.Items[0].UserId);
        Assert.HasCount(1, directory.Calls);
    }

    [TestMethod]
    public async Task UserUnit_ListAsync_propagates_cancellation_to_host_directory()
    {
        var directory = new RecordingHostUserDisplayDirectory(
            new HostUserDirectoryEntry(UserA, "user-a", "User A"));
        var query = CreateUserUnitQueryExecutor(
            total: 1,
            rows: [CreateUserUnitRow(UserA)]);
        var service = CreateUserUnitQueryService(query, directory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ListAsync(
                CurrentUserId,
                isSuperAdministrator: true,
                page: 1,
                pageSize: 10,
                userId: null,
                unitId: null,
                cts.Token));
    }

    [TestMethod]
    public async Task UserUnit_ListAsync_clamps_page_size_to_directory_batch_upper_bound()
    {
        var directory = new RecordingHostUserDisplayDirectory();
        var rows = Enumerable.Range(0, 120)
            .Select(_ => CreateUserUnitRow(Guid.NewGuid()))
            .ToArray();
        var query = CreateUserUnitQueryExecutor(total: rows.Length, rows: rows);
        var service = CreateUserUnitQueryService(query, directory);

        var result = await service.ListAsync(
            CurrentUserId,
            isSuperAdministrator: true,
            page: 1,
            pageSize: 500,
            userId: null,
            unitId: null,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(100, query.LastPageSize);
        Assert.IsLessThanOrEqualTo(100, result.Value!.Items.Count);
        Assert.IsLessThanOrEqualTo(100, directory.Calls[0].UserIds.Length);
    }

    private static TenantUserUnitQueryService CreateUserUnitQueryService(
        RecordingOrganizationQueryExecutor query,
        RecordingHostUserDisplayDirectory directory)
    {
        var scopeResolver = Substitute.For<IUserDataScopeResolver>();
        scopeResolver.ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(new EffectiveUserDataScope(
                IsUnrestricted: true,
                RoleScopes: []));
        var scopeFilterBuilder = Substitute.For<IDataScopeSqlFilterBuilder>();
        scopeFilterBuilder.BuildOrganizationUnitFilter(
                Arg.Any<EffectiveUserDataScope>(),
                Arg.Any<string>(),
                Arg.Any<Guid>())
            .Returns((DataScopeSqlFilter?)null);
        return new TenantUserUnitQueryService(
            query,
            directory,
            scopeResolver,
            scopeFilterBuilder,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
    }

    private static TenantUserPositionQueryService CreateUserPositionQueryService(
        RecordingOrganizationQueryExecutor query,
        RecordingHostUserDisplayDirectory directory) =>
        new(
            query,
            directory,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

    private static RecordingOrganizationQueryExecutor CreateUserUnitQueryExecutor(
        long total,
        IReadOnlyList<OrganizationUserUnitListRow> rows) =>
        new(
            countStatement: OrganizationSql.CountUserUnits,
            listStatement: OrganizationSql.ListUserUnitsSqlServer,
            total,
            rows);

    private static RecordingOrganizationQueryExecutor CreateUserPositionQueryExecutor(
        long total,
        IReadOnlyList<OrganizationUserPositionListRow> rows) =>
        new(
            countStatement: OrganizationSql.CountUserPositions,
            listStatement: OrganizationSql.ListUserPositionsSqlServer,
            total,
            rows);

    private static OrganizationUserUnitListRow CreateUserUnitRow(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        UnitId = UnitId,
        UnitCode = "unit-a",
        UnitName = "Unit A",
        IsPrimary = true,
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
        Version = 1,
    };

    private static OrganizationUserPositionListRow CreateUserPositionRow(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        PositionId = Guid.Parse("019bc2b1-2a40-7cc3-8992-a80de51bf295"),
        PositionCode = "position-a",
        PositionName = "Position A",
        IsPrimary = true,
        IsActive = true,
        CreatedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
        Version = 1,
    };

    private sealed class RecordingHostUserDisplayDirectory : IHostUserDisplayDirectory
    {
        private readonly Dictionary<Guid, HostUserDirectoryEntry> users;

        public RecordingHostUserDisplayDirectory(params HostUserDirectoryEntry[] entries) =>
            users = entries.ToDictionary(entry => entry.Id);

        public List<HostUserBatchCall> Calls { get; } = [];

        public Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindHostUsersAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>>(
                    cancellationToken);
            }

            Calls.Add(new HostUserBatchCall(userIds.Distinct().ToArray()));
            IReadOnlyDictionary<Guid, HostUserDirectoryEntry> result = userIds
                .Distinct()
                .Where(users.ContainsKey)
                .ToDictionary(userId => userId, userId => users[userId]);
            return Task.FromResult(result);
        }
    }

    private sealed record HostUserBatchCall(Guid[] UserIds);

    private sealed class RecordingOrganizationQueryExecutor : IQueryExecutor
    {
        private readonly SqlStatement countStatement;
        private readonly SqlStatement listStatement;
        private readonly long total;
        private readonly object listRows;

        public RecordingOrganizationQueryExecutor(
            SqlStatement countStatement,
            SqlStatement listStatement,
            long total,
            object listRows)
        {
            this.countStatement = countStatement;
            this.listStatement = listStatement;
            this.total = total;
            this.listRows = listRows;
        }

        public int LastPageSize { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == countStatement)
            {
                return Task.FromResult((T?)(object)total);
            }

            return Task.FromResult<T?>(default);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement == listStatement && parameters is not null)
            {
                var parameterType = parameters.GetType();
                LastPageSize = (int)parameterType.GetProperty("PageSize")!.GetValue(parameters)!;
                var offset = (int)parameterType.GetProperty("Offset")!.GetValue(parameters)!;
                if (listRows is IReadOnlyList<T> typedRows)
                {
                    var page = typedRows.Skip(offset).Take(LastPageSize).ToArray();
                    return Task.FromResult<IReadOnlyList<T>>(page);
                }
            }

            if (statement == listStatement && listRows is IReadOnlyList<T> fallbackRows)
            {
                return Task.FromResult<IReadOnlyList<T>>(fallbackRows);
            }

            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }
    }
}