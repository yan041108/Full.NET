using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class FieldProjectionResolverTests
{
    private static readonly Guid UserId =
        Guid.Parse("01981a3f-00c0-7000-8000-000000000071");

    [TestMethod]
    public void Host_users_catalog_exposes_stable_keys_without_physical_identifiers()
    {
        var resource = FieldProjectionCatalog.CreateDefault()
            .GetRequiredResource(FieldProjectionResourceKeys.HostUsers);

        CollectionAssert.AreEqual(
            new[]
            {
                "address",
                "birth_date",
                "created_at_utc",
                "display_name",
                "education_level",
                "email",
                "emergency_contact",
                "emergency_contact_address",
                "emergency_contact_phone",
                "employee_number",
                "ethnicity",
                "failed_login_count",
                "gender",
                "graduated_school",
                "id",
                "id_card_number",
                "id_card_type",
                "is_active",
                "join_date_utc",
                "lockout_end_utc",
                "nickname",
                "office_phone",
                "phone_number",
                "political_status",
                "preferred_locale",
                "remark",
                "sort_order",
                "updated_at_utc",
                "username",
                "version",
            },
            resource.Fields.Select(field => field.FieldKey).Order().ToArray());
        Assert.IsFalse(resource.Fields.Any(field =>
            field.FieldKey.Contains("fn_identity", StringComparison.Ordinal)
            || field.FieldKey.Contains("password", StringComparison.Ordinal)
            || field.FieldKey.Contains("security_stamp", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task No_role_grants_returns_only_mandatory_fields()
    {
        var resolver = CreateResolver([]);

        var projection = await resolver.ResolveAsync(
            UserId,
            tenantId: null,
            FieldProjectionResourceKeys.HostUsers,
            default);

        CollectionAssert.AreEqual(
            new[]
            {
                "created_at_utc",
                "display_name",
                "id",
                "is_active",
                "updated_at_utc",
                "username",
                "version",
            },
            projection.FieldKeys.ToArray());
    }

    [TestMethod]
    public async Task Active_host_roles_union_known_grants_and_ignore_unknown_rows()
    {
        var resolver = CreateResolver([
            HostRow("preferred_locale"),
            HostRow("failed_login_count"),
            HostRow("removed_database_column"),
            HostRow("preferred_locale"),
        ]);

        var projection = await resolver.ResolveAsync(
            UserId,
            tenantId: null,
            FieldProjectionResourceKeys.HostUsers,
            default);

        CollectionAssert.Contains(projection.FieldKeys.ToArray(), "preferred_locale");
        CollectionAssert.Contains(projection.FieldKeys.ToArray(), "failed_login_count");
        CollectionAssert.DoesNotContain(
            projection.FieldKeys.ToArray(),
            "removed_database_column");
    }

    [TestMethod]
    public async Task Mismatched_scope_or_tenant_cannot_expand_host_projection()
    {
        var tenantId = Guid.Parse("01981a3f-00c0-7000-8000-000000000072");
        var resolver = CreateResolver([
            new UserFieldProjectionGrantRow("tenant", tenantId, false, "failed_login_count"),
            new UserFieldProjectionGrantRow("host", tenantId, true, null),
        ]);

        var projection = await resolver.ResolveAsync(
            UserId,
            tenantId: null,
            FieldProjectionResourceKeys.HostUsers,
            default);

        CollectionAssert.DoesNotContain(projection.FieldKeys.ToArray(), "failed_login_count");
        CollectionAssert.DoesNotContain(projection.FieldKeys.ToArray(), "lockout_end_utc");
    }

    [TestMethod]
    public async Task Host_super_administrator_receives_all_grantable_fields()
    {
        var resolver = CreateResolver([
            new UserFieldProjectionGrantRow("host", null, true, null),
        ]);

        var projection = await resolver.ResolveAsync(
            UserId,
            tenantId: null,
            FieldProjectionResourceKeys.HostUsers,
            default);

        CollectionAssert.AreEqual(
            FieldProjectionCatalog.CreateDefault()
                .GetRequiredResource(FieldProjectionResourceKeys.HostUsers)
                .Fields
                .Select(field => field.FieldKey)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            projection.FieldKeys.ToArray());
    }

    [TestMethod]
    public async Task Resolver_reloads_role_grants_on_every_request()
    {
        var query = new StubQueryExecutor([HostRow("preferred_locale")]);
        var resolver = new UserFieldProjectionResolver(
            query,
            FieldProjectionCatalog.CreateDefault());

        await resolver.ResolveAsync(
            UserId,
            null,
            FieldProjectionResourceKeys.HostUsers,
            default);
        await resolver.ResolveAsync(
            UserId,
            null,
            FieldProjectionResourceKeys.HostUsers,
            default);

        Assert.AreEqual(2, query.QueryCount);
    }

    private static UserFieldProjectionResolver CreateResolver(
        IReadOnlyList<UserFieldProjectionGrantRow> rows) =>
        new(new StubQueryExecutor(rows), FieldProjectionCatalog.CreateDefault());

    private static UserFieldProjectionGrantRow HostRow(string fieldKey) =>
        new("host", null, false, fieldKey);

    private sealed class StubQueryExecutor(
        IReadOnlyList<UserFieldProjectionGrantRow> rows) : IQueryExecutor
    {
        public int QueryCount { get; private set; }

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
            Assert.AreEqual(IdentitySql.GetUserFieldProjectionGrants, statement);
            Assert.AreEqual(UserId, parameters?.GetType().GetProperty("UserId")?.GetValue(parameters));
            return Task.FromResult<IReadOnlyList<T>>(rows.Cast<T>().ToArray());
        }
    }
}
