using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFolders;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

/// <summary>同一场景验证双库原生游标、目录资源上界及原子批量成员撤销；Unit 不能证明 SQL 和真实锁行为。</summary>
internal static class PerformanceFollowupAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, DatabaseProvider provider, string database, Action<string> evidence)
    {
        var a = await factory.CreateHostIdentityAsync($"followup-a-{Guid.NewGuid():N}", []);
        var b = await factory.CreateHostIdentityAsync($"followup-b-{Guid.NewGuid():N}", []);
        var c = await factory.CreateHostIdentityAsync($"followup-c-{Guid.NewGuid():N}", []);
        // 先证明同一令牌可用，后面拒绝才能归因于本次会话撤销，而不是时钟推进或缺少授权。
        foreach (var actor in new[] { a, b, c })
        {
            using var client = factory.CreateClientForHost("localhost");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", actor.AccessToken);
            using var response = await client.GetAsync("/api/v1/me");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        await PerformanceSummaryPagingAssertions.VerifyAsync(commands, queries, provider, evidence);
        await PerformanceWebhookBatchAssertions.VerifyAsync(scope.ServiceProvider, provider, evidence);
        await VerifyCursorAsync(commands, queries, provider, database, evidence);
        await VerifyFoldersAsync(scope.ServiceProvider, commands, a.UserId, provider, database, evidence);
        var roleA = await queries.QuerySingleOrDefaultAsync<Guid>(Sql("role_for_user", "SELECT RoleId FROM fn_identity_user_role WHERE UserId=@UserId"), P(("UserId", a.UserId)));
        var roleC = await queries.QuerySingleOrDefaultAsync<Guid>(Sql("role_for_user", "SELECT RoleId FROM fn_identity_user_role WHERE UserId=@UserId"), P(("UserId", c.UserId)));
        Assert.AreNotEqual(Guid.Empty, roleA); Assert.AreNotEqual(Guid.Empty, roleC);
        var beforeStamp = await StampAsync(queries, a.UserId);
        var initialVersion = await RoleVersionAsync(queries, roleA);
        var service = scope.ServiceProvider.GetRequiredService<HostRoleMembersService>();
        var missing = await service.ReplaceAsync(roleA, new ReplaceHostRoleMembersRequest([a.UserId, Guid.NewGuid()], initialVersion));
        Assert.IsFalse(missing.IsSuccess);
        Assert.AreEqual(initialVersion, await RoleVersionAsync(queries, roleA), "失败 Result 必须回滚提前取得写锁的角色版本。");
        Assert.AreEqual(beforeStamp, await StampAsync(queries, a.UserId));
        // 不同角色共享用户，验证完整并集锁序；不重试死锁、不将异常降级为通过。
        for (var round = 0; round < 12; round++)
        {
            Assert.IsTrue((await service.ReplaceAsync(roleA, new ReplaceHostRoleMembersRequest([a.UserId], await RoleVersionAsync(queries, roleA)))).IsSuccess);
            Assert.IsTrue((await service.ReplaceAsync(roleC, new ReplaceHostRoleMembersRequest([c.UserId], await RoleVersionAsync(queries, roleC)))).IsSuccess);
            var versions = new[] { await RoleVersionAsync(queries, roleA), await RoleVersionAsync(queries, roleC) };
            await Task.WhenAll(ReplaceInScopeAsync(factory, roleA, [b.UserId], versions[0]),
                ReplaceInScopeAsync(factory, roleC, [a.UserId, b.UserId], versions[1]));
        }
        await PerformanceAssertions.CapturePlanAsync(provider, database,
            provider == DatabaseProvider.MySql ? HostRoleMembersSql.ValidateUsersMySql : HostRoleMembersSql.ValidateUsersSqlServer,
            P(("UserIdsJson", JsonSerializer.Serialize(new[] { a.UserId, b.UserId, c.UserId }))), evidence);
        var candidatePage = await service.ListAsync(roleA,1,1,userIds:[a.UserId,b.UserId]);
        Assert.IsTrue(candidatePage.IsSuccess);
        Assert.AreEqual(1L,candidatePage.Value!.Total);
        Assert.HasCount(1,candidatePage.Value.Items);
        Assert.AreEqual(b.UserId,candidatePage.Value.Items[0].UserId);
        var emptyCandidates = await service.ListAsync(roleA,1,20,userIds:[]);
        Assert.IsTrue(emptyCandidates.IsSuccess);
        Assert.AreEqual(0L,emptyCandidates.Value!.Total);
        Assert.IsEmpty(emptyCandidates.Value.Items);
        if (provider == DatabaseProvider.SqlServer)
            await PerformanceAssertions.CapturePlanAsync(provider, database, HostRoleMembersSql.InsertMembersSqlServer,
                P(("RoleId", roleA), ("UserIdsJson", JsonSerializer.Serialize(new[] { a.UserId, b.UserId }))), evidence);
        var rollbackActor = await factory.CreateHostIdentityAsync($"rollback-{Guid.NewGuid():N}", []);
        var rollbackVersion = await RoleVersionAsync(queries, roleA);
        var rollbackStamp = await StampAsync(queries, rollbackActor.UserId);
        var failing = ActivatorUtilities.CreateInstance<HostRoleMembersService>(scope.ServiceProvider, new FailingCommands(commands));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => failing.ReplaceAsync(roleA,
            new ReplaceHostRoleMembersRequest([rollbackActor.UserId], rollbackVersion)));
        Assert.AreEqual(rollbackVersion, await RoleVersionAsync(queries, roleA));
        Assert.AreEqual(rollbackStamp, await StampAsync(queries, rollbackActor.UserId));
        Assert.AreEqual(1L, await queries.QuerySingleOrDefaultAsync<long>(Sql("rollback_members", "SELECT COUNT(*) FROM fn_identity_user_role WHERE RoleId=@RoleId AND UserId=@UserId"), P(("RoleId", roleA), ("UserId", b.UserId))));
        Assert.AreEqual(1L, await queries.QuerySingleOrDefaultAsync<long>(Sql("rollback_session", "SELECT COUNT(*) FROM fn_identity_refresh_session WHERE UserId=@UserId AND RevokedAtUtc IS NULL"), P(("UserId", rollbackActor.UserId))));
        foreach (var actor in new[] { a, b, c })
        {
            Assert.AreEqual(0L, await queries.QuerySingleOrDefaultAsync<long>(Sql("active_sessions", "SELECT COUNT(*) FROM fn_identity_refresh_session WHERE UserId=@UserId AND RevokedAtUtc IS NULL"), P(("UserId", actor.UserId))));
            using var client = factory.CreateClientForHost("localhost");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", actor.AccessToken);
            using var response = await client.GetAsync("/api/v1/me");
            Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode, "旧会话不能在批量撤销后继续读取当前用户。");
        }
        Assert.AreNotEqual(beforeStamp, await StampAsync(queries, a.UserId));
        // 用户页赋权推进同一角色版本；陈旧候选页不能覆盖赋权，刷新后仍保留未见成员。
        var candidateVersion = await RoleVersionAsync(queries, roleA);
        var userRoles = scope.ServiceProvider.GetRequiredService<HostUserRolesService>();
        var currentRoles = await userRoles.GetAsync(c.UserId);
        Assert.IsTrue(currentRoles.IsSuccess);
        Assert.IsTrue((await userRoles.ReplaceAsync(c.UserId,
            new ReplaceHostUserRolesRequest([roleA], currentRoles.Value!.Version))).IsSuccess);
        var staleCandidates = await service.ReplaceAsync(roleA,
            new ReplaceHostRoleMembersRequest([a.UserId], candidateVersion) { CandidateUserIds = [a.UserId, b.UserId] });
        Assert.IsFalse(staleCandidates.IsSuccess);
        Assert.IsTrue((await service.ReplaceAsync(roleA,
            new ReplaceHostRoleMembersRequest([a.UserId], await RoleVersionAsync(queries, roleA))
                { CandidateUserIds = [a.UserId, b.UserId] })).IsSuccess);
        Assert.AreEqual(1L,await queries.QuerySingleOrDefaultAsync<long>(Sql("unseen_member_preserved",
            "SELECT COUNT(*) FROM fn_identity_user_role WHERE RoleId=@RoleId AND UserId=@UserId"), P(("RoleId",roleA),("UserId",c.UserId))));
        evidence("followup role_members user_entry_version_conflict=verified unseen_member_preserved=verified");
        // 超过一个分块的真实成员集，覆盖集合插入、独立戳和不变成员再次撤销的数据库路径。
        var bulkIds = Enumerable.Range(0, 1001).Select(_ => Guid.CreateVersion7()).ToArray();
        var bulkSource = provider == DatabaseProvider.SqlServer
            ? "SELECT Id,CONCAT('bulk-',CONVERT(varchar(36),Id)) AS Username FROM OPENJSON(@Ids) WITH (Id uniqueidentifier '$')"
            : "SELECT UUID_TO_BIN(j.Id,0) AS Id,CONCAT('bulk-',j.Id) AS Username FROM JSON_TABLE(@Ids,'$[*]' COLUMNS(Id CHAR(36) PATH '$')) AS j";
        await commands.ExecuteAsync(Sql("bulk_users", $"""
            INSERT INTO fn_identity_user
                (Id,TenantId,ScopeKey,Username,NormalizedUsername,DisplayName,PasswordHash,IsActive,
                 FailedLoginCount,LockoutEndUtc,SecurityStamp,CreatedAtUtc,UpdatedAtUtc,Version,
                 PreferredLocale,ProfileVersion,AccountType,MustChangePassword,PasswordChangedAtUtc)
            SELECT j.Id,NULL,'host',j.Username,UPPER(j.Username),j.Username,u.PasswordHash,1,
                   0,NULL,u.SecurityStamp,@Now,NULL,1,u.PreferredLocale,1,u.AccountType,0,u.PasswordChangedAtUtc
            FROM ({bulkSource}) AS j CROSS JOIN fn_identity_user AS u WHERE u.Id=@SourceId
            """), P(("Ids", JsonSerializer.Serialize(bulkIds)), ("SourceId", a.UserId), ("Now", DateTimeOffset.UtcNow)));
        Assert.IsTrue((await service.ReplaceAsync(roleA,
            new ReplaceHostRoleMembersRequest(bulkIds, await RoleVersionAsync(queries, roleA)))).IsSuccess);
        Assert.AreEqual(1001L, await queries.QuerySingleOrDefaultAsync<long>(Sql("bulk_member_count",
            "SELECT COUNT(*) FROM fn_identity_user_role WHERE RoleId=@RoleId"), P(("RoleId", roleA))));
        Assert.AreEqual(1001L, await queries.QuerySingleOrDefaultAsync<long>(Sql("bulk_distinct_stamps",
            "SELECT COUNT(DISTINCT u.SecurityStamp) FROM fn_identity_user AS u INNER JOIN fn_identity_user_role AS r ON r.UserId=u.Id WHERE r.RoleId=@RoleId"), P(("RoleId", roleA))));
        var unchangedStamp = await StampAsync(queries, bulkIds[^1]);
        Assert.IsTrue((await service.ReplaceAsync(roleA,
            new ReplaceHostRoleMembersRequest(bulkIds, await RoleVersionAsync(queries, roleA)))).IsSuccess);
        Assert.AreNotEqual(unchangedStamp, await StampAsync(queries, bulkIds[^1]));
        evidence("followup role_members concurrent_rounds=12 errors=0 retries=0 session_revocation=verified rollback=verified");
        evidence("followup role_members bulk_members=1001 distinct_stamps=1001 unchanged_member_rotation=verified");
    }

    private static async Task ReplaceInScopeAsync(FullNetApiFactory factory, Guid role, Guid[] ids, int version)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var result = await scope.ServiceProvider.GetRequiredService<HostRoleMembersService>().ReplaceAsync(role, new ReplaceHostRoleMembersRequest(ids, version));
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);
    }

    private static Task<int> RoleVersionAsync(IQueryExecutor queries, Guid id) => queries.QuerySingleOrDefaultAsync<int>(Sql("role_version", "SELECT Version FROM fn_identity_role WHERE Id=@Id"), P(("Id", id)));
    private static Task<string?> StampAsync(IQueryExecutor queries, Guid id) => queries.QuerySingleOrDefaultAsync<string>(Sql("user_stamp", "SELECT SecurityStamp FROM fn_identity_user WHERE Id=@Id"), P(("Id", id)));

    private static async Task VerifyCursorAsync(ICommandExecutor commands, IQueryExecutor queries, DatabaseProvider provider, string database, Action<string> evidence)
    {
        var tenantIds = Enumerable.Range(0, 11).Select(_ => Guid.CreateVersion7()).ToArray();
        foreach (var tenant in tenantIds)
        {
            var parameters = P(("Id", Guid.NewGuid()), ("TenantId", tenant), ("SourceId", Guid.NewGuid()), ("Actor", Guid.NewGuid()), ("Now", DateTimeOffset.UtcNow));
            await commands.ExecuteAsync(Sql("seed_import", """
                INSERT INTO fn_import_export_task(Id,TenantId,SchemaKey,SchemaDisplayName,WorksheetKey,SourceFileId,StatusKey,TotalRows,ValidRowCount,InvalidRowCount,RequestedByUserId,CreatedAtUtc,Version)
                VALUES(@Id,@TenantId,'followup','followup','Sheet1',@SourceId,'queued',1,1,0,@Actor,@Now,1)
                """), parameters);
            parameters["Id"] = Guid.NewGuid();
            await commands.ExecuteAsync(Sql("seed_export", """
                INSERT INTO fn_reporting_export_task(Id,TenantId,DefinitionId,VersionNumber,DefinitionKey,DefinitionName,FormatKey,ParametersJson,StatusKey,RequestedByUserId,CreatedAtUtc)
                VALUES(@Id,@TenantId,@SourceId,1,'followup','followup','excel','{}','queued',@Actor,@Now)
                """), parameters);
        }
        foreach (var statement in new[] { provider == DatabaseProvider.MySql ? ImportExportTaskSql.ListPendingTenantIdsMySql : ImportExportTaskSql.ListPendingTenantIdsSqlServer,
            provider == DatabaseProvider.MySql ? ReportingExportTaskSql.ListPendingTenantIdsMySql : ReportingExportTaskSql.ListPendingTenantIdsSqlServer })
        {
            var seen = new HashSet<Guid>(); Guid? after = null;
            for (var page = 0; page < 5; page++)
            {
                var p = P(("BatchSize", 3), ("Now", DateTimeOffset.UtcNow), ("AfterTenantId", after));
                var rows = await queries.QueryAsync<Guid>(statement, p);
                Assert.IsLessThanOrEqualTo(3, rows.Count);
                foreach (var id in rows) Assert.IsTrue(seen.Add(id), "原生 UUID 游标不能重复读取候选。");
                if (rows.Count == 0) break;
                after = rows[^1];
                if (page == 0) await PerformanceAssertions.CapturePlanAsync(provider, database, statement, p, evidence);
            }
            Assert.AreEqual(tenantIds.Length, seen.Count);
        }
        evidence("followup tenant_cursor candidates=11 page_limit=3 duplicates=0 provider_native_order=verified");
    }

    private static async Task VerifyFoldersAsync(IServiceProvider services, ICommandExecutor commands, Guid actor, DatabaseProvider provider, string database, Action<string> evidence)
    {
        var management = services.GetRequiredService<HostFolderManagementService>();
        var query = services.GetRequiredService<HostFolderQueryService>();
        var root = await management.CreateAsync(actor, new CreateHostFolderRequest(null, $"followup-{Guid.NewGuid():N}")); Assert.IsTrue(root.IsSuccess);
        var parent = root.Value!.Id;
        for (var depth = 2; depth <= HostFolderQueryService.MaximumDepth; depth++)
        {
            var child = await management.CreateAsync(actor, new CreateHostFolderRequest(parent, $"level-{depth}")); Assert.IsTrue(child.IsSuccess);
            parent = child.Value!.Id;
        }
        var tooDeep = await management.CreateAsync(actor, new CreateHostFolderRequest(parent, "too-deep")); Assert.IsFalse(tooDeep.IsSuccess);
        var children = await query.GetTreeAsync(root.Value.Id); Assert.HasCount(1, children.Value!); Assert.IsTrue(children.Value![0].HasChildren);
        var statement = provider == DatabaseProvider.MySql ? HostFolderSql.ListChildrenMySql : HostFolderSql.ListChildrenSqlServer;
        await PerformanceAssertions.CapturePlanAsync(provider, database, statement, P(("ParentId", root.Value.Id), ("TakeCount", 1001)), evidence);
        var rows = JsonSerializer.Serialize(Enumerable.Range(0, 1000).Select(i => new { id = Guid.NewGuid(), name = $"bulk-{i}", order = i }));
        var source = provider == DatabaseProvider.MySql
            ? "JSON_TABLE(@RowsJson,'$[*]' COLUMNS(Id CHAR(36) PATH '$.id',Name VARCHAR(128) PATH '$.name',DisplayOrder INT PATH '$.order')) AS j"
            : "OPENJSON(@RowsJson) WITH (Id uniqueidentifier '$.id',Name nvarchar(128) '$.name',DisplayOrder int '$.order') AS j";
        var idSql = provider == DatabaseProvider.MySql ? "UUID_TO_BIN(j.Id,0)" : "j.Id";
        await commands.ExecuteAsync(Sql("seed_folders", $"""
            INSERT INTO fn_files_folder(Id,TenantId,ParentId,Name,DisplayOrder,Revision,CreatedAtUtc,CreatedByUserId)
            SELECT {idSql},NULL,@ParentId,j.Name,j.DisplayOrder,0,@Now,@Actor FROM {source}
            """), P(("RowsJson", rows), ("ParentId", root.Value.Id), ("Actor", actor), ("Now", DateTimeOffset.UtcNow)));
        var overBudget = await query.GetTreeAsync(root.Value.Id); Assert.IsFalse(overBudget.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FolderQueryLimitExceeded, overBudget.Error!.Code);
        evidence("followup folders depth_limit=32 direct_children_limit=1000 flat_response=verified oversize=explicit_rejection");
    }

    private sealed class FailingCommands(ICommandExecutor inner) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement == HostRoleMembersSql.RevokeSessionsMySql || statement == HostRoleMembersSql.RevokeSessionsSqlServer)
                throw new InvalidOperationException("Injected revocation failure after member and stamp writes.");
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }

    private static SqlStatement Sql(string key, string text) => new($"test.performance_followup.{key}", text, SqlDataScope.Global);
    private static Dictionary<string, object?> P(params (string Key, object? Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
}
