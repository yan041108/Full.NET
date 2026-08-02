using System.Data.Common;
using Dapper;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>
/// 为生命周期 SQL 运行时矩阵提供稳定的显式 Schema 与生成产物解析，避免与 Unit 测试项目耦合。
/// </summary>
internal static class LifecycleRuntimeSqlTestSupport
{
    internal static FullNetCrudSchema CreateLifecycleSchema() =>
        FullNetCrudSchema.CreateProject(
            ownerKey: "acme",
            moduleKey: "catalog",
            entityKey: "product",
            databaseTableName: "acme_catalog_product",
            rootNamespace: "Acme.Modules.Catalog",
            clrTypeName: "Product",
            apiResourceName: "products",
            permissionResourceName: "products",
            FullNetCrudDataScope.TenantRequired,
            new FullNetCrudEntityCapabilities(
                FullNetCrudDeleteMode.SoftDelete,
                HasCreatedAudit: true,
                HasUpdatedAudit: true,
                HasDeletedAudit: true,
                HasVersion: true,
                FullNetCrudOwnershipMode.None),
            FullNetCrudScene.Single,
            [],
            [
                new("Id", "Id", "id", FullNetScalarType.Uuid),
                new("TenantId", "TenantId", "tenantId", FullNetScalarType.Uuid),
                new(
                    "Name",
                    "Name",
                    "displayName",
                    FullNetScalarType.String,
                    MaxLength: 200),
                new("Version", "Version", "version", FullNetScalarType.Int64),
                new(
                    "CreatedAtUtc",
                    "CreatedAtUtc",
                    "createdAtUtc",
                    FullNetScalarType.DateTimeUtc),
                new(
                    "CreatedById",
                    "CreatedById",
                    "createdById",
                    FullNetScalarType.Uuid),
                new(
                    "UpdatedAtUtc",
                    "UpdatedAtUtc",
                    "updatedAtUtc",
                    FullNetScalarType.DateTimeUtc,
                    IsNullable: true),
                new(
                    "UpdatedById",
                    "UpdatedById",
                    "updatedById",
                    FullNetScalarType.Uuid,
                    IsNullable: true),
                new(
                    "IsDeleted",
                    "IsDeleted",
                    "isDeleted",
                    FullNetScalarType.Boolean),
                new(
                    "DeletedAtUtc",
                    "DeletedAtUtc",
                    "deletedAtUtc",
                    FullNetScalarType.DateTimeUtc,
                    IsNullable: true),
                new(
                    "DeletedById",
                    "DeletedById",
                    "deletedById",
                    FullNetScalarType.Uuid,
                    IsNullable: true),
            ]);

    internal static IReadOnlyList<GeneratedArtifact> GenerateArtifacts(
        FullNetCrudSchema schema) =>
        CrudArtifactGenerator.Generate(schema);

    internal static string RequireMigrationSql(
        IReadOnlyList<GeneratedArtifact> artifacts,
        bool sqlServer) =>
        artifacts
            .Single(artifact =>
                artifact.RelativePath.Contains(
                    sqlServer ? "SqlServer" : "MySql",
                    StringComparison.Ordinal))
            .Content;

    internal static string RequireSqlConstant(
        IReadOnlyList<GeneratedArtifact> artifacts,
        string constantName)
    {
        var sqlSource = artifacts
            .Single(artifact =>
                artifact.RelativePath.EndsWith(
                    "ProductSql.g.cs",
                    StringComparison.Ordinal))
            .Content;
        return ExtractSqlConstant(sqlSource, constantName);
    }

    internal static string ExtractSqlConstant(string source, string constantName)
    {
        var opener = $"public const string {constantName} = \"\"\"";
        var start = source.IndexOf(opener, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"生成 SQL 缺少常量 {constantName}。");
        }

        start += opener.Length;
        if (start < source.Length && source[start] == '\n')
        {
            start++;
        }

        var end = source.IndexOf("\"\"\";", start, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException(
                $"生成 SQL 常量 {constantName} 未正确结束。");
        }

        return source[start..end].TrimEnd();
    }

    internal static async Task AssertSoftDeleteLifecycleMatrixAsync(
        Func<Task<string>> createConnectionStringAsync,
        Func<string, DbConnection> createConnection,
        bool sqlServer)
    {
        var schema = CreateLifecycleSchema();
        var artifacts = GenerateArtifacts(schema);
        var migrationSql = RequireMigrationSql(artifacts, sqlServer);
        var insertSql = RequireSqlConstant(artifacts, "Insert");
        var updateSql = RequireSqlConstant(artifacts, "Update");
        var deleteSql = RequireSqlConstant(artifacts, "Delete");
        var findByIdSql = RequireSqlConstant(artifacts, "FindById");

        var connectionString = await createConnectionStringAsync();
        await using var connection = createConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(migrationSql);

        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        Assert.AreEqual(
            1,
            await connection.ExecuteAsync(
                insertSql,
                new
                {
                    Id = entityId,
                    TenantId = tenantId,
                    Name = "Probe",
                    Version = 1L,
                    CreatedAtUtc = createdAt,
                    CreatedById = actorId,
                    UpdatedAtUtc = (DateTimeOffset?)null,
                    UpdatedById = (Guid?)null,
                    IsDeleted = false,
                    DeletedAtUtc = (DateTimeOffset?)null,
                    DeletedById = (Guid?)null,
                }));

        var updatedAt = createdAt.AddMinutes(1);
        Assert.AreEqual(
            1,
            await connection.ExecuteAsync(
                updateSql,
                new
                {
                    Id = entityId,
                    TenantId = tenantId,
                    Name = "Updated",
                    Version = 1L,
                    UpdatedAtUtc = updatedAt,
                    UpdatedById = actorId,
                }));

        Assert.AreEqual(
            0,
            await connection.ExecuteAsync(
                updateSql,
                new
                {
                    Id = entityId,
                    TenantId = tenantId,
                    Name = "Stale",
                    Version = 1L,
                    UpdatedAtUtc = updatedAt,
                    UpdatedById = actorId,
                }));

        var deletedAt = updatedAt.AddMinutes(1);
        Assert.AreEqual(
            1,
            await connection.ExecuteAsync(
                deleteSql,
                new
                {
                    Id = entityId,
                    TenantId = tenantId,
                    Version = 2L,
                    DeletedAtUtc = deletedAt,
                    DeletedById = actorId,
                }));

        var activeRow = await connection.QuerySingleOrDefaultAsync<LifecycleRow>(
            findByIdSql,
            new { Id = entityId, TenantId = tenantId });
        Assert.IsNull(activeRow);

        Assert.AreEqual(
            0,
            await connection.ExecuteAsync(
                updateSql,
                new
                {
                    Id = entityId,
                    TenantId = tenantId,
                    Name = "AfterDelete",
                    Version = 3L,
                    UpdatedAtUtc = deletedAt,
                    UpdatedById = actorId,
                }));

        var wrongTenantRow = await connection.QuerySingleOrDefaultAsync<LifecycleRow>(
            findByIdSql,
            new { Id = entityId, TenantId = Guid.NewGuid() });
        Assert.IsNull(wrongTenantRow);
    }

    private sealed class LifecycleRow
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public string Name { get; init; } = string.Empty;

        public long Version { get; init; }

        public bool IsDeleted { get; init; }
    }
}