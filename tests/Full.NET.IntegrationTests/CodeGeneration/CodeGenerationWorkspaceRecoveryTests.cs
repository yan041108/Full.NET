using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.Dapper;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Git;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>真实双库读取已有 running 行并完成同一工作区操作，覆盖数据库时间精度与终态故障后的重启恢复。</summary>
[TestClass]
public sealed class CodeGenerationWorkspaceRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Real_run_row_is_reused_after_terminal_failure_across_provider_timestamp_precision(DatabaseProvider provider)
    {
        var root = Directory.CreateTempSubdirectory("fullnet-codegen-db-recovery-").FullName;
        var archiveRoot = Directory.CreateTempSubdirectory("fullnet-codegen-db-archives-").FullName;
        try
        {
            var connectionString = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
                : new MySqlConnectionStringBuilder(await SharedDatabaseFixture.CreateMySqlDatabaseAsync()) { GuidFormat = MySqlGuidFormat.Binary16 }.ConnectionString;
            await using DbConnection connection = provider == DatabaseProvider.SqlServer ? new SqlConnection(connectionString) : new MySqlConnection(connectionString);
            await connection.OpenAsync();
            // 此夹具只创建运行投影需要的表形状；不把夹具 DDL 计作迁移恢复验收。
            var uuid = provider == DatabaseProvider.SqlServer ? "uniqueidentifier" : "BINARY(16)";
            var text = provider == DatabaseProvider.SqlServer ? "nvarchar" : "varchar";
            var time = provider == DatabaseProvider.SqlServer ? "datetimeoffset(7)" : "datetime(6)";
            var primary = provider == DatabaseProvider.SqlServer ? "PRIMARY KEY CLUSTERED" : "PRIMARY KEY";
            await connection.ExecuteAsync($"""
                CREATE TABLE fn_codegeneration_run (
                  Id {uuid} NOT NULL {primary}, TemplateId {uuid} NULL, TemplateVersion bigint NULL,
                  SourceApplyRunId {uuid} NULL, OperationKind {text}(32) NOT NULL, Status {text}(16) NOT NULL,
                  ModuleKey {text}(64) NULL, EntityKey {text}(64) NULL, SchemaSha256 char(64) NULL,
                  ArtifactCount int NOT NULL, ManifestSha256 char(64) NULL, ErrorCode {text}(128) NULL,
                  RequestedByUserId {uuid} NOT NULL, StartedAtUtc {time} NOT NULL, FinishedAtUtc {time} NOT NULL
                )
                """);
            var repository = FindRepository();
            var migration = await File.ReadAllTextAsync(Path.Combine(repository, "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",
                provider.ToString(), "290_CodeGenerationRunArchive.sql"));
            // 先执行首块再完整重跑两次，验证创建或注释提交后 DbUp 未记账的恢复。
            var batches = provider == DatabaseProvider.SqlServer ? System.Text.RegularExpressions.Regex.Split(migration, @"(?im)^GO\s*$") : [migration];
            await connection.ExecuteAsync(batches[0]);
            foreach (var batch in batches.Where(batch => !string.IsNullOrWhiteSpace(batch))) await connection.ExecuteAsync(batch);
            foreach (var batch in batches.Where(batch => !string.IsNullOrWhiteSpace(batch))) await connection.ExecuteAsync(batch);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Provider"] = provider.ToString(), ["Database:ConnectionString"] = connectionString, ["Database:MySqlGuidStorageMode"] = "Binary16" }).Build();
            var registrations = new ServiceCollection();
            registrations.AddLogging();
            registrations.AddScoped<CurrentTenantAccessor>();
            registrations.AddScoped<ICurrentTenant>(services => services.GetRequiredService<CurrentTenantAccessor>());
            registrations.AddFullNetDapper(configuration, "Testing");
            await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var scope = services.CreateAsyncScope();
            ((ICurrentTenantContextWriter)scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>()).SetHost();
            var commands = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var fault = new FailFirstCompletion(commands);
            var clock = new PreciseClock();
            var archives = new CodeGenerationArtifactStore(queries, commands, Options.Create(new CodeGenerationArtifactOptions { RootDirectory = archiveRoot }), clock);
            var id = Guid.CreateVersion7();
            var git = new CodeGenerationGitWorkspaceService(Options.Create(new CodeGenerationGitOptions()),
                Options.Create(new CodeGenerationApplyOptions { Enabled = true, WorkspaceRoot = root }),
                new ProcessCodeGenerationGitCommandRunner(), NullLogger<CodeGenerationGitWorkspaceService>.Instance);
            var lease = await GenerationWorkspaceLease.TryAcquireAsync(root) ?? throw new InvalidOperationException();
            try
            {
                GeneratedArtifact[] artifacts = [new("service.g.cs", GeneratedArtifactKind.Backend, "frozen-source")];
                var snapshot = await GenerationWorkspaceStore.CaptureAsync(root, artifacts);
                var plan = GenerationWritePlanner.Plan(artifacts, snapshot.ExistingFiles, snapshot.PreviousManifest);
                var record = new CodeGenerationRunRecord { Id = id, TemplateId = Guid.CreateVersion7(), TemplateVersion = 1, OperationKind = "apply", Status = "running",
                    ModuleKey = "catalog", EntityKey = "product", SchemaSha256 = GenerationContentHash.Compute("{}"), ArtifactCount = 1,
                    ManifestSha256 = CodeGenerationRunSummary.ComputeManifestSha256(plan.NextManifest!), RequestedByUserId = Guid.CreateVersion7(),
                    StartedAtUtc = clock.UtcNow, FinishedAtUtc = clock.UtcNow };
                var context = new CodeGenerationWorkspaceRunContext(1, record, Guid.CreateVersion7(), clock.UtcNow.AddMinutes(15),
                    CrudArtifactGenerator.GeneratorVersion, "{}", null, git.CapturePublicationContext(id, "database recovery", plan));
                await GenerationWorkspaceJournal.PrepareAsync(lease, id, id, "apply", "host", CodeGenerationWorkspaceRecovery.SerializeContext(context), plan);
                await Assert.ThrowsExactlyAsync<IOException>(() => CodeGenerationWorkspaceRecovery.RecoverAsync(lease, root, fault, queries, git, clock, archives, CancellationToken.None));
                Assert.AreEqual("frozen-source", await File.ReadAllTextAsync(Path.Combine(root, "service.g.cs")));
                var saved = await queries.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById, CodeGenerationSqlParameters.Create(("Id", id)));
                Assert.IsNotNull(saved);
                Assert.AreEqual("running", saved.Status);
                if (provider == DatabaseProvider.MySql) Assert.AreEqual(0L, saved.StartedAtUtc.UtcTicks % TimeSpan.TicksPerMicrosecond);
            }
            finally { await lease.DisposeAsync(); }
            // 新文件租约与真实数据库投影相遇；不重插行、不重做计划，也不能因 MySQL 微秒精度被误判为摘要漂移。
            await using var recoveryLease = await GenerationWorkspaceLease.TryAcquireAsync(root) ?? throw new InvalidOperationException();
            await CodeGenerationWorkspaceRecovery.RecoverAsync(recoveryLease, root, commands, queries, git, clock, archives, CancellationToken.None);
            var finished = await queries.QuerySingleOrDefaultAsync<CodeGenerationRunRecord>(CodeGenerationRunSql.FindById, CodeGenerationSqlParameters.Create(("Id", id)));
            Assert.AreEqual("succeeded", finished!.Status);
            Assert.AreEqual(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_codegeneration_run"));
            Assert.IsNull(await GenerationWorkspaceJournal.ReadPendingAsync(recoveryLease));
            var archive = await archives.ReadAsync(id, default);
            Assert.AreEqual(finished.ManifestSha256, archive.Record.ManifestSha256);
            Assert.AreEqual(1, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_codegeneration_run_archive"));
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(archive.Bytes));
            using var reader = new StreamReader(zip.GetEntry("service.g.cs")!.Open());
            Assert.AreEqual("frozen-source", await reader.ReadToEndAsync());
            Assert.IsNotNull(zip.GetEntry(GenerationWorkspaceStore.ManifestRelativePath));
            var rollbackId = Guid.CreateVersion7();
            await connection.ExecuteAsync("""
                INSERT INTO fn_codegeneration_run (Id, TemplateId, TemplateVersion, SourceApplyRunId, OperationKind, Status,
                    ModuleKey, EntityKey, SchemaSha256, ArtifactCount, ManifestSha256, ErrorCode, RequestedByUserId, StartedAtUtc, FinishedAtUtc)
                SELECT @RollbackId, TemplateId, TemplateVersion, Id, 'rollback', 'succeeded', ModuleKey, EntityKey,
                    SchemaSha256, ArtifactCount, ManifestSha256, NULL, RequestedByUserId, StartedAtUtc, FinishedAtUtc
                FROM fn_codegeneration_run WHERE Id = @ApplyId
                """, new { RollbackId = rollbackId, ApplyId = id });
            var candidates = await queries.QueryAsync<CodeGenerationCheckpointCleanupCandidate>(provider == DatabaseProvider.SqlServer
                ? CodeGenerationRunSql.ListEligibleCheckpointCleanupSqlServer : CodeGenerationRunSql.ListEligibleCheckpointCleanupMySql,
                CodeGenerationSqlParameters.Create(("Take", 10), ("CutoffUtc", clock.UtcNow.AddDays(1))));
            Assert.AreEqual(id, candidates.Single().ApplyRunId);
            Assert.AreEqual(rollbackId, candidates.Single().RollbackRunId);
        }
        finally { Directory.Delete(root, recursive: true); Directory.Delete(archiveRoot, recursive: true); }
    }

    private static string FindRepository([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFile)!, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) return directory.FullName;
        throw new InvalidOperationException("找不到迁移源码根目录。");
    }

    private sealed class PreciseClock : IClock
    {
        public DateTimeOffset UtcNow => new DateTimeOffset(2026, 10, 26, 7, 0, 0, TimeSpan.Zero).AddTicks(1234567);
    }

    private sealed class FailFirstCompletion(ICommandExecutor inner) : ICommandExecutor
    {
        private bool _failed;
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (statement == CodeGenerationRunSql.CompleteApply && !_failed) { _failed = true; throw new IOException("模拟数据库终态暂时不可用。"); }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}
