using System.Data.Common;
using Dapper;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Modules.CodeGeneration.Persistence;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Data;

/// <summary>真实双库验证创建身份唯一约束和当前发布的迁移就绪门禁。</summary>
[TestClass]
public sealed class ReviewRemediationDatabaseTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Missing_migration_blocks_readiness_and_payment_identity_is_unique(DatabaseProvider provider)
    {
        var connectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connectionString);
        await factory.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString)
            : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        var health = factory.Services.GetRequiredService<HealthCheckService>();
        async Task<HealthStatus> Status() => (await health.CheckHealthAsync(registration => registration.Name == "database-schema-contract")).Status;
        Assert.AreEqual(HealthStatus.Healthy, await Status());
        var table = provider == DatabaseProvider.SqlServer ? "dbo.SchemaVersions" : "schemaversions";
        var script = DatabaseSchemaRequirements.Read(provider, new()).Single(name => name.EndsWith("322_PaymentOrderIdempotency.sql", StringComparison.Ordinal));
        Assert.AreEqual(1, await db.ExecuteAsync($"DELETE FROM {table} WHERE ScriptName=@script", new { script }));
        Assert.AreEqual(HealthStatus.Unhealthy, await Status());
        await db.ExecuteAsync($"INSERT INTO {table}(ScriptName,Applied) VALUES(@script,@now)", new { script, now = DateTime.UtcNow });
        Assert.AreEqual(HealthStatus.Healthy, await Status());

        const string insert = """
            INSERT INTO fn_payment_order(Id,TenantId,MerchantConfigId,ChannelKey,OutTradeNo,TradeStateKey,
                AmountMinor,Currency,Subject,CreatedAtUtc,Version,IdempotencyKey,RequestHash)
            VALUES(@id,@tenant,@merchant,'wechat_native',@number,'created',1,'CNY','test',@now,1,@key,@hash)
            """;
        var tenant = Guid.CreateVersion7(); var key = Guid.CreateVersion7(); var merchant = Guid.CreateVersion7();
        object Input(Guid scope) => new { id = Guid.CreateVersion7(), tenant = scope, merchant,
            number = Guid.NewGuid().ToString("N"), now = DateTime.UtcNow, key, hash = new string('a', 64) };
        await db.ExecuteAsync(insert, Input(tenant));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync(insert, Input(tenant)));
        await db.ExecuteAsync(insert, Input(Guid.CreateVersion7()));
        Assert.AreEqual(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_payment_order WHERE IdempotencyKey=@key", new { key }));
        // 替换和清除使用读取版本；过期删除不得解除刚刚更新的引用。
        var userId = (await db.QueryAsync<Guid>("SELECT Id FROM fn_identity_user WHERE ScopeKey='host'")).First();
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_identity_user_profile WHERE UserId=@userId", new { userId }) == 0)
            await db.ExecuteAsync("INSERT INTO fn_identity_user_profile(UserId,SortOrder,Version) VALUES(@userId,100,1)", new { userId });
        var profileVersion = await db.ExecuteScalarAsync<int>("SELECT Version FROM fn_identity_user_profile WHERE UserId=@userId", new { userId });
        var fileId = Guid.CreateVersion7();
        Assert.AreEqual(1, await db.ExecuteAsync(IdentitySql.UpdateHostUserProfileAvatar.Text, new { UserId = userId, AvatarFileId = fileId, Version = profileVersion }));
        Assert.AreEqual(0, await db.ExecuteAsync(IdentitySql.ClearHostUserProfileAvatar.Text, new { UserId = userId, Version = profileVersion }));
        Assert.AreEqual(1, await db.ExecuteAsync(IdentitySql.ClearHostUserProfileAvatar.Text, new { UserId = userId, Version = profileVersion + 1 }));
        Assert.AreEqual(1, await db.ExecuteAsync(IdentitySql.UpdateHostUserProfileSignature.Text, new { UserId = userId, SignatureFileId = fileId, Version = profileVersion + 2 }));
        Assert.AreEqual(0, await db.ExecuteAsync(IdentitySql.ClearHostUserProfileSignature.Text, new { UserId = userId, Version = profileVersion + 2 }));
        Assert.AreEqual(1, await db.ExecuteAsync(IdentitySql.ClearHostUserProfileSignature.Text, new { UserId = userId, Version = profileVersion + 3 }));
        var brandingTenant = (await db.QueryAsync<Guid>("SELECT Id FROM fn_tenancy_tenant")).First();
        await db.ExecuteAsync("UPDATE fn_tenancy_tenant SET LogoFileId=@fileId WHERE Id=@brandingTenant", new { fileId, brandingTenant });
        var brandingVersion = await db.ExecuteScalarAsync<int>("SELECT Version FROM fn_tenancy_tenant WHERE Id=@brandingTenant", new { brandingTenant });
        foreach (var statement in new[] { TenantSql.ClearTenantLogo, TenantSql.ClearTenantLogoCurrent })
            Assert.AreEqual(0, await db.ExecuteAsync(statement.Text, new { TenantId = brandingTenant, Version = brandingVersion - 1, UpdatedAtUtc = DateTime.UtcNow }));
        Assert.AreEqual(1, await db.ExecuteAsync(TenantSql.ClearTenantLogoCurrent.Text, new { TenantId = brandingTenant, Version = brandingVersion, UpdatedAtUtc = DateTime.UtcNow }));

        // 失败运行仍保留定位检查点的身份；终态更新只生效一次。
        var run = Guid.CreateVersion7();
        await db.ExecuteAsync(CodeGenerationRunSql.Insert.Text, new
        {
            Id = run, TemplateId = Guid.CreateVersion7(), TemplateVersion = 1,
            SourceApplyRunId = (Guid?)null, OperationKind = "apply", Status = "running",
            ModuleKey = "catalog", EntityKey = "product", SchemaSha256 = new string('a', 64),
            ArtifactCount = 1, ManifestSha256 = new string('b', 64), ErrorCode = (string?)null,
            RequestedByUserId = userId, StartedAtUtc = DateTime.UtcNow, FinishedAtUtc = DateTime.UtcNow,
        });
        var failure = new { Id = run, ErrorCode = "codegen.apply.failed", FinishedAtUtc = DateTime.UtcNow };
        Assert.AreEqual(1, await db.ExecuteAsync(CodeGenerationRunSql.FailApply.Text, failure));
        Assert.AreEqual(0, await db.ExecuteAsync(CodeGenerationRunSql.FailApply.Text, failure));
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_codegeneration_run WHERE Id=@run AND Status='failed' AND ModuleKey='catalog' AND SchemaSha256 IS NOT NULL AND ManifestSha256 IS NOT NULL", new { run }));
    }
}
