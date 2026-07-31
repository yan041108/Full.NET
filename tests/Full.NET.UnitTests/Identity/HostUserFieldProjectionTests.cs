using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class HostUserFieldProjectionTests
{
    private static readonly Guid ActorUserId = Guid.CreateVersion7();
    private static readonly Guid TargetUserId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Restricted_columns_are_not_queried_without_effective_grants()
    {
        var query = new RecordingQueryExecutor();
        var service = CreateService(query, MandatoryProjection());

        var result = await service.ListAsync(ActorUserId, 1, 20);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserPreferredLocales));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserFailedLoginCounts));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserLockoutEnds));
        Assert.IsNull(result.Value!.Items.Single().ProjectedFields!.PreferredLocale);
    }

    [TestMethod]
    public async Task Only_the_explicitly_effective_restricted_column_is_queried()
    {
        var query = new RecordingQueryExecutor();
        var projection = MandatoryProjection() with
        {
            FieldKeys = [.. MandatoryProjection().FieldKeys, "preferred_locale"],
        };
        var service = CreateService(query, projection);

        var result = await service.GetByIdAsync(ActorUserId, TargetUserId);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("zh-CN", result.Value!.ProjectedFields!.PreferredLocale);
        CollectionAssert.Contains(
            result.Value.ProjectedFields.EffectiveFieldKeys.ToArray(),
            "preferred_locale");
        Assert.IsTrue(query.Statements.Contains(IdentitySql.ListHostUserPreferredLocales));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserFailedLoginCounts));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserLockoutEnds));
    }

    [TestMethod]
    public async Task Export_uses_the_same_effective_projection_as_list_and_detail()
    {
        var query = new RecordingQueryExecutor();
        var projection = MandatoryProjection() with
        {
            FieldKeys = [.. MandatoryProjection().FieldKeys, "failed_login_count"],
        };
        var service = CreateService(query, projection);

        var result = await service.ExportAsync(ActorUserId);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(query.Statements.Contains(IdentitySql.ListHostUserFailedLoginCounts));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserPreferredLocales));
        Assert.IsFalse(query.Statements.Contains(IdentitySql.ListHostUserLockoutEnds));
        CollectionAssert.Contains(
            result.Value!.Single().ProjectedFields!.EffectiveFieldKeys.ToArray(),
            "failed_login_count");
    }

    private static HostUserQueryService CreateService(
        IQueryExecutor query,
        UserFieldProjection projection)
    {
        var resolver = Substitute.For<IUserFieldProjectionResolver>();
        resolver.ResolveAsync(
                ActorUserId,
                null,
                FieldProjectionResourceKeys.HostUsers,
                Arg.Any<CancellationToken>())
            .Returns(projection);
        return new HostUserQueryService(
            query,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            resolver);
    }

    private static UserFieldProjection MandatoryProjection() =>
        new(
            FieldProjectionResourceKeys.HostUsers,
            [
                "created_at_utc",
                "display_name",
                "id",
                "is_active",
                "updated_at_utc",
                "username",
                "version",
            ]);

    private sealed class RecordingQueryExecutor : IQueryExecutor
    {
        public List<SqlStatement> Statements { get; } = [];

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            object? value = statement == IdentitySql.CountHostUsers
                ? 1L
                : statement == IdentitySql.FindHostUserProjectionBaseById
                    ? CreateRow()
                    : null;
            return Task.FromResult((T?)value);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(statement);
            object[] values = statement == IdentitySql.ListHostUsersSqlServer
                ? [CreateRow()]
                : statement == IdentitySql.ListHostUserPreferredLocales
                    ? [new HostUserPreferredLocaleRow { Id = TargetUserId, Value = "zh-CN" }]
                    : statement == IdentitySql.ListHostUserFailedLoginCounts
                        ? [new HostUserFailedLoginCountRow { Id = TargetUserId, Value = 2 }]
                    : [];
            return Task.FromResult<IReadOnlyList<T>>(values.Cast<T>().ToArray());
        }

        private static HostUserListRow CreateRow() => new()
        {
            Id = TargetUserId,
            Username = "projection-user",
            DisplayName = "Projection User",
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            Version = 1,
        };
    }
}
