using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dapper;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUserPositions.Queries;
using Full.NET.Modules.Organization.Features.ManageTenantUserUnits.Queries;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Organization;

/// <summary>SQL Server 用户隶属列表的聚焦验收。</summary>
[TestClass]
public sealed class OrganizationAssignmentListApiSqlServerTests
{
    [TestMethod]
    public Task Assignment_lists_search_all_pages_and_preserve_boundaries_with_sql_server() =>
        OrganizationAssignmentListQueryAssertions.VerifyAsync(DatabaseProvider.SqlServer);
}

/// <summary>MySQL 用户隶属列表的聚焦验收。</summary>
[TestClass]
public sealed class OrganizationAssignmentListApiMySqlTests
{
    [TestMethod]
    public Task Assignment_lists_search_all_pages_and_preserve_boundaries_with_mysql() =>
        OrganizationAssignmentListQueryAssertions.VerifyAsync(DatabaseProvider.MySql);
}

/// <summary>真实双库验证静态 JOIN、分页统计及组织范围；单元测试不能证明 SQL 方言和 UUID JSON 关联。</summary>
internal static class OrganizationAssignmentListQueryAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var resolver = new TestScopeResolver();
        using var factory = new FullNetApiFactory(provider, connectionString,
            configureTestServices: services => services.Replace(ServiceDescriptor.Singleton<IUserDataScopeResolver>(resolver)));
        await factory.InitializeAsync();
        await using DbConnection database = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        await database.OpenAsync();
        var tenantId = await database.QuerySingleAsync<Guid>("SELECT Id FROM fn_tenancy_tenant WHERE Identifier = 'acme'");
        var actorId = await database.QuerySingleAsync<Guid>("SELECT Id FROM fn_identity_user WHERE Username = 'admin' AND ScopeKey = 'host' AND TenantId IS NULL");
        var disabledUserId = Guid.CreateVersion7();
        var tenantUserId = Guid.CreateVersion7();
        await InsertUserAsync(database, actorId, disabledUserId, null, "host", "joined-disabled", "停用可见");
        await InsertUserAsync(database, actorId, tenantUserId, tenantId, "tenant-test", "joined-tenant", "租户账号");

        var units = Enumerable.Range(0, 25).Select(_ => Guid.CreateVersion7()).ToArray();
        var positions = Enumerable.Range(0, 25).Select(_ => Guid.CreateVersion7()).ToArray();
        for (var index = 0; index < 25; index++)
        {
            await database.ExecuteAsync("""
                INSERT INTO fn_organization_unit (Id, TenantId, ParentId, Code, Name, DisplayOrder, IsActive, CreatedAtUtc, Version)
                VALUES (@Id, @TenantId, @ParentId, @Code, @Name, 0, @IsActive, @Now, 1)
                """, new { Id = units[index], TenantId = tenantId, ParentId = index is 1 or 2 ? (Guid?)units[0] : index == 3 ? units[2] : null,
                    Code = $"joined-{index:D2}", Name = index == 24 ? "目标!%_[机构" : "分页机构", IsActive = index != 2, Now = DateTime.UtcNow });
            await database.ExecuteAsync("""
                INSERT INTO fn_organization_position (Id, TenantId, Code, Name, DisplayOrder, IsActive, CreatedAtUtc, Version)
                VALUES (@Id, @TenantId, @Code, @Name, 0, 1, @Now, 1)
                """, new { Id = positions[index], TenantId = tenantId, Code = $"joined-{index:D2}", Name = index == 24 ? "目标!%_[职位" : "分页职位", Now = DateTime.UtcNow });
            await InsertAssignmentsAsync(database, tenantId, actorId, units[index], positions[index], index == 0, index != 24);
        }

        await InsertAssignmentsAsync(database, tenantId, disabledUserId, units[24], positions[24], false, true);
        // 孤儿、非 Host 用户和其他租户的关联均不得污染列表或总数。
        await InsertAssignmentsAsync(database, tenantId, Guid.CreateVersion7(), units[24], positions[24], false, true);
        await InsertAssignmentsAsync(database, tenantId, tenantUserId, units[24], positions[24], false, true);
        var foreign = new { TenantId = Guid.CreateVersion7(), UnitId = Guid.CreateVersion7(), PositionId = Guid.CreateVersion7(), Name = "其他租户", Now = DateTime.UtcNow };
        await database.ExecuteAsync("""
            INSERT INTO fn_organization_unit (Id, TenantId, ParentId, Code, Name, DisplayOrder, IsActive, CreatedAtUtc, Version)
            VALUES (@UnitId, @TenantId, NULL, 'joined-24', @Name, 0, 1, @Now, 1)
            """, foreign);
        await database.ExecuteAsync("""
            INSERT INTO fn_organization_position (Id, TenantId, Code, Name, DisplayOrder, IsActive, CreatedAtUtc, Version)
            VALUES (@PositionId, @TenantId, 'joined-24', @Name, 0, 1, @Now, 1)
            """, foreign);
        await InsertAssignmentsAsync(database, foreign.TenantId, actorId, foreign.UnitId, foreign.PositionId, false, true);

        await using var scope = factory.Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        try
        {
            var unitQuery = scope.ServiceProvider.GetRequiredService<TenantUserUnitListQueryService>();
            var positionQuery = scope.ServiceProvider.GetRequiredService<TenantUserPositionListQueryService>();
            var first = (await unitQuery.ListAsync(actorId, false, 1, 20, null, null, "admin")).Value!;
            var second = (await unitQuery.ListAsync(actorId, false, 2, 20, null, null, "admin")).Value!;
            Assert.AreEqual(25L, first.Total);
            Assert.AreEqual(first.Total, second.Total);
            Assert.HasCount(20, first.Items);
            Assert.HasCount(5, second.Items);
            Assert.IsFalse(first.Items.Select(row => row.Id).Intersect(second.Items.Select(row => row.Id)).Any());
            CollectionAssert.AreEqual(second.Items.Select(row => row.Id).ToArray(),
                (await unitQuery.ListAsync(actorId, false, 2, 20, null, null, "admin")).Value!.Items.Select(row => row.Id).ToArray());
            Assert.AreEqual(26L, (await unitQuery.ListAsync(actorId, false, 1, 100, null, null)).Value!.Total);
            var filteredUnits = (await unitQuery.ListAsync(actorId, false, 1, 20, null, null, "ADMIN", "JOINED-24", false)).Value!;
            Assert.AreEqual(1L, filteredUnits.Total);
            Assert.AreEqual(units[24], filteredUnits.Items.Single().UnitId);
            Assert.IsFalse(filteredUnits.Items.Single().IsActive);
            Assert.AreEqual(disabledUserId, (await unitQuery.ListAsync(actorId, false, 1, 20, null, null, "停用可见")).Value!.Items.Single().UserId);
            Assert.AreEqual(2L, (await unitQuery.ListAsync(actorId, false, 1, 20, null, null, unitSearch: "!%_[")).Value!.Total);
            var missing = (await unitQuery.ListAsync(actorId, false, 1, 20, null, null, "不存在")).Value!;
            Assert.AreEqual(0L, missing.Total);
            Assert.IsEmpty(missing.Items);
            Assert.IsEmpty((await unitQuery.ListAsync(actorId, false, 3, 20, null, null, "admin")).Value!.Items);

            var positionFirst = (await positionQuery.ListAsync(1, 20, null, null, "admin")).Value!;
            var positionSecond = (await positionQuery.ListAsync(2, 20, null, null, "admin")).Value!;
            Assert.AreEqual(25L, positionFirst.Total);
            Assert.AreEqual(positionFirst.Total, positionSecond.Total);
            Assert.HasCount(20, positionFirst.Items);
            Assert.HasCount(5, positionSecond.Items);
            Assert.IsFalse(positionFirst.Items.Select(row => row.Id).Intersect(positionSecond.Items.Select(row => row.Id)).Any());
            Assert.AreEqual(26L, (await positionQuery.ListAsync(1, 100, null, null)).Value!.Total);
            var filteredPositions = (await positionQuery.ListAsync(1, 20, null, null, "ADMIN", "JOINED-24", false)).Value!;
            Assert.AreEqual(1L, filteredPositions.Total);
            Assert.AreEqual(positions[24], filteredPositions.Items.Single().PositionId);
            Assert.IsFalse(filteredPositions.Items.Single().IsActive);
            Assert.AreEqual(disabledUserId, (await positionQuery.ListAsync(1, 20, null, null, "停用可见")).Value!.Items.Single().UserId);
            Assert.AreEqual(2L, (await positionQuery.ListAsync(1, 20, null, null, positionSearch: "!%_[")).Value!.Total);

            resolver.Scope = new(false, []);
            var denied = (await unitQuery.ListAsync(actorId, false, 1, 100, null, null)).Value!;
            Assert.AreEqual(0L, denied.Total);
            Assert.IsEmpty(denied.Items);
            Assert.AreEqual(26L, (await positionQuery.ListAsync(1, 100, null, null)).Value!.Total);
            resolver.Scope = new(false, [new(Guid.NewGuid(), RoleDataScopeKinds.Self)]);
            Assert.AreEqual(24L, (await unitQuery.ListAsync(actorId, false, 1, 100, null, null)).Value!.Total);
            resolver.Scope = new(false, [new(Guid.NewGuid(), RoleDataScopeKinds.Organization)]);
            Assert.AreEqual(units[0], (await unitQuery.ListAsync(actorId, false, 1, 100, null, null)).Value!.Items.Single().UnitId);
            resolver.Scope = new(false, [new(Guid.NewGuid(), RoleDataScopeKinds.OrganizationSubtree)]);
            CollectionAssert.AreEquivalent(new[] { units[0], units[1] },
                (await unitQuery.ListAsync(actorId, false, 1, 100, null, null)).Value!.Items.Select(row => row.UnitId).ToArray());

            var roleId = await database.QueryFirstAsync<Guid>("SELECT Id FROM fn_identity_role WHERE ScopeKey = 'host' AND TenantId IS NULL");
            await database.ExecuteAsync("INSERT INTO fn_identity_role_data_scope_unit (RoleId, UnitId) VALUES (@RoleId, @UnitId)", new { RoleId = roleId, UnitId = units[24] });
            resolver.Scope = new(false, [new(roleId, RoleDataScopeKinds.Custom), new(roleId, RoleDataScopeKinds.OrganizationSubtree)]);
            Assert.AreEqual(4L, (await unitQuery.ListAsync(actorId, false, 1, 100, null, null)).Value!.Total);
            resolver.Scope = new(true, []);
        }
        finally
        {
            currentTenant.Clear();
        }

        // HTTP 另验新增参数与输入校验；真实授权及拒绝权限仍由既有管理生命周期用例覆盖。
        using var client = factory.CreateClientForHost("localhost");
        var hostToken = await factory.CreateHostAccessTokenAsync([
            "platform.dashboard.read", "tenancy.tenants.read", "tenancy.tenants.switch",
            OrganizationUserUnitManagementPermissions.Read, OrganizationUserPositionManagementPermissions.Read]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
        using var enter = await client.PutAsJsonAsync("/api/v1/tenancy/context", new ChangeTenantContextRequest(tenantId));
        Assert.AreEqual(HttpStatusCode.OK, enter.StatusCode, await enter.Content.ReadAsStringAsync());
        var token = (await enter.Content.ReadFromJsonAsync<TenantContextTokenResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var entity in new[] { "unit", "position" })
        {
            using var response = await client.GetAsync($"/api/v1/organization/user-{entity}s?page=1&pageSize=20&userSearch=admin&{entity}Search=joined-24&isActive=false");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
            using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual(1L, body.RootElement.GetProperty("total").GetInt64());
            Assert.AreEqual(1, body.RootElement.GetProperty("items").GetArrayLength());
            Assert.IsFalse(body.RootElement.GetProperty("items")[0].GetProperty("isActive").GetBoolean());
            using var invalid = await client.GetAsync($"/api/v1/organization/user-{entity}s?userSearch={new string('x', 101)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using var documentResponse = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, documentResponse.StatusCode);
        using var document = System.Text.Json.JsonDocument.Parse(await documentResponse.Content.ReadAsStringAsync());
        foreach (var entity in new[] { "unit", "position" })
        {
            var operation = document.RootElement.GetProperty("paths")
                .GetProperty($"/api/v1/organization/user-{entity}s").GetProperty("get");
            CollectionAssert.AreEquivalent(new[] { "page", "pageSize", "userId", $"{entity}Id", "userSearch", $"{entity}Search", "isActive" },
                operation.GetProperty("parameters").EnumerateArray().Select(parameter => parameter.GetProperty("name").GetString()!).ToArray());
            Assert.IsTrue(operation.GetProperty("responses").TryGetProperty("400", out _));
        }
    }

    private static async Task InsertUserAsync(DbConnection database, Guid actorId, Guid userId, Guid? tenantId, string scopeKey, string username, string displayName)
    {
        await database.ExecuteAsync("""
            INSERT INTO fn_identity_user
                (Id, TenantId, ScopeKey, Username, NormalizedUsername, DisplayName,
                 PasswordHash, IsActive, FailedLoginCount, SecurityStamp, CreatedAtUtc, Version,
                 PreferredLocale, ProfileVersion, AccountType, MustChangePassword, PasswordChangedAtUtc)
            SELECT @Id, @TenantId, @ScopeKey, @Username, @NormalizedUsername, @DisplayName,
                   PasswordHash, 0, 0, @Stamp, CreatedAtUtc, 1, PreferredLocale, 1, AccountType, 0, PasswordChangedAtUtc
            FROM fn_identity_user WHERE Id = @ActorId
            """, new { Id = userId, TenantId = tenantId, ScopeKey = scopeKey, Username = username, NormalizedUsername = username.ToUpperInvariant(), DisplayName = displayName, Stamp = Guid.NewGuid().ToString("N"), ActorId = actorId });
    }

    private static async Task InsertAssignmentsAsync(DbConnection database, Guid tenantId, Guid userId, Guid unitId, Guid positionId, bool primary, bool active)
    {
        await database.ExecuteAsync("""
            INSERT INTO fn_organization_user_unit (Id, TenantId, UserId, UnitId, IsPrimary, IsActive, CreatedAtUtc, Version)
            VALUES (@Id, @TenantId, @UserId, @UnitId, @IsPrimary, @IsActive, @Now, 1)
            """, new { Id = Guid.CreateVersion7(), TenantId = tenantId, UserId = userId, UnitId = unitId, IsPrimary = primary, IsActive = active, Now = DateTime.UtcNow });
        await database.ExecuteAsync("""
            INSERT INTO fn_organization_user_position (Id, TenantId, UserId, PositionId, IsPrimary, IsActive, CreatedAtUtc, Version)
            VALUES (@Id, @TenantId, @UserId, @PositionId, @IsPrimary, @IsActive, @Now, 1)
            """, new { Id = Guid.CreateVersion7(), TenantId = tenantId, UserId = userId, PositionId = positionId, IsPrimary = primary, IsActive = active, Now = DateTime.UtcNow });
    }

    private sealed class TestScopeResolver : IUserDataScopeResolver
    {
        internal EffectiveUserDataScope Scope { get; set; } = new(true, []);

        public Task<EffectiveUserDataScope> ResolveAsync(Guid userId, bool isSuperAdministrator, CancellationToken cancellationToken = default) => Task.FromResult(Scope);
    }
}
