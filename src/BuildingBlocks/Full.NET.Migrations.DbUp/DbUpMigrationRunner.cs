using DbUp;
using DbUp.Builder;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.Migrations.DbUp;

public sealed class DbUpMigrationRunner : IDatabaseMigrationRunner
{
    private readonly IOptions<DatabaseOptions> _databaseOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly UuidBinaryContractOptions _contractOptions;
    private readonly PreV1NamingContractOptions _namingContractOptions;

    public DbUpMigrationRunner(
        IOptions<DatabaseOptions> databaseOptions,
        ILoggerFactory loggerFactory)
        : this(
            databaseOptions,
            loggerFactory,
            Options.Create(new UuidBinaryContractOptions()),
            Options.Create(new PreV1NamingContractOptions()))
    {
    }

    public DbUpMigrationRunner(
        IOptions<DatabaseOptions> databaseOptions,
        ILoggerFactory loggerFactory,
        IOptions<UuidBinaryContractOptions> contractOptions)
        : this(
            databaseOptions,
            loggerFactory,
            contractOptions,
            Options.Create(new PreV1NamingContractOptions()))
    {
    }

    public DbUpMigrationRunner(
        IOptions<DatabaseOptions> databaseOptions,
        ILoggerFactory loggerFactory,
        IOptions<UuidBinaryContractOptions> contractOptions,
        IOptions<PreV1NamingContractOptions> namingContractOptions)
    {
        _databaseOptions = databaseOptions;
        _loggerFactory = loggerFactory;
        _contractOptions = contractOptions.Value;
        _namingContractOptions = namingContractOptions.Value;
    }

    public async Task<MigrationResult> MigrateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateContractOptions(_contractOptions);
        ValidateNamingContractOptions(_namingContractOptions);
        var options = _databaseOptions.Value;
        var (builder, providerSegment) = CreateBuilder(options);
        if (options.Provider == DatabaseProvider.MySql)
        {
            // 094 已进入 DbUp journal 契约，禁止改写资源文件；兼容预处理只移除
            // MySQL 8 不支持的语法，实际约束由追加迁移 095 收敛。
            builder.WithPreprocessor(
                new MySqlPublishedMigrationCompatibilityPreprocessor());
        }
        var upgrader = builder
            .WithExecutionTimeout(TimeSpan.FromSeconds(options.CommandTimeoutSeconds))
            .WithScriptsEmbeddedInAssembly(
                MigrationAssembly.Value,
                name => name.Contains(providerSegment, StringComparison.Ordinal))
            .WithVariable("UuidContractMaintenanceMode", ToSqlBoolean(_contractOptions.MaintenanceMode))
            .WithVariable("UuidContractBackupVerified", ToSqlBoolean(_contractOptions.BackupVerified))
            .WithVariable("UuidContractLegacyWritersStopped", ToSqlBoolean(_contractOptions.LegacyWritersStopped))
            .WithVariable("UuidContractDestructiveDdlApprovalId", _contractOptions.DestructiveDdlApprovalId)
            .WithVariable(
                "PreV1NamingContractMaintenanceMode",
                ToSqlBoolean(_namingContractOptions.MaintenanceMode))
            .WithVariable(
                "PreV1NamingContractBackupVerified",
                ToSqlBoolean(_namingContractOptions.BackupVerified))
            .WithVariable(
                "PreV1NamingContractLegacyWritersStopped",
                ToSqlBoolean(_namingContractOptions.LegacyWritersStopped))
            .WithVariable(
                "PreV1NamingContractLegacyOutboxDrained",
                ToSqlBoolean(_namingContractOptions.LegacyOutboxDrained))
            .WithVariable(
                "PreV1NamingContractDestructiveDdlApprovalId",
                _namingContractOptions.DestructiveDdlApprovalId)
            .LogTo(_loggerFactory)
            .Build();

        var result = upgrader.PerformUpgrade();
        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        if (options.Provider == DatabaseProvider.MySql)
        {
            await VerifyMySqlSchemaModeAsync(options, cancellationToken);
        }

        return new MigrationResult(true, result.Scripts.Count());
    }

    private static void ValidateContractOptions(UuidBinaryContractOptions options)
    {
        if (!string.IsNullOrEmpty(options.DestructiveDdlApprovalId)
            && !UuidBinaryContractOptions.IsApprovalIdValid(options.DestructiveDdlApprovalId))
        {
            throw new InvalidOperationException(
                "UuidBinaryContract:DestructiveDdlApprovalId 格式无效。");
        }
    }

    private static void ValidateNamingContractOptions(PreV1NamingContractOptions options)
    {
        if (!string.IsNullOrEmpty(options.DestructiveDdlApprovalId)
            && !UuidBinaryContractOptions.IsApprovalIdValid(options.DestructiveDdlApprovalId))
        {
            throw new InvalidOperationException(
                "PreV1NamingContract:DestructiveDdlApprovalId 格式无效。");
        }
    }

    private static string ToSqlBoolean(bool value) => value ? "1" : "0";

    private static async Task VerifyMySqlSchemaModeAsync(
        DatabaseOptions options,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                options.ConnectionString,
                options.MySqlGuidStorageMode,
                allowUserVariables: false));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'fn_uuid_contract_state'
            """;
        var stateTableExists = Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
        var schemaMode = string.Empty;
        if (stateTableExists)
        {
            command.CommandText =
                "SELECT COALESCE(SchemaMode, '') FROM fn_uuid_contract_state WHERE Id = 1";
            schemaMode = Convert.ToString(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }
        var expectsBinary = options.MySqlGuidStorageMode == MySqlGuidStorageMode.Binary16;
        var isBinary = string.Equals(schemaMode, "Binary16", StringComparison.Ordinal);
        if (expectsBinary != isBinary)
        {
            throw new InvalidOperationException(
                "MySQL UUID 应用模式与数据库 Contract schema 状态不一致。");
        }
    }

    private static (UpgradeEngineBuilder Builder, string ProviderSegment) CreateBuilder(
        DatabaseOptions options)
    {
        switch (options.Provider)
        {
            case DatabaseProvider.SqlServer:
                return (
                    DeployChanges.To.SqlDatabase(options.ConnectionString),
                    ".Migrations.SqlServer.");

            case DatabaseProvider.MySql:
                return (
                    DeployChanges.To.MySqlDatabase(
                        MySqlConnectionStringPolicy.Create(
                            options.ConnectionString,
                            options.MySqlGuidStorageMode,
                            allowUserVariables: true)),
                    ".Migrations.MySql.");

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Provider,
                    "Unsupported database provider.");
        }
    }
}
