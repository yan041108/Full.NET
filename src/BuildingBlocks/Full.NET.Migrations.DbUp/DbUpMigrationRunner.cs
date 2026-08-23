using DbUp;
using DbUp.Builder;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.Migrations.DbUp;

/// <summary>
/// DbUp 实现的数据库迁移器：按当前 Provider 加载嵌入资源脚本、注入 Contract 维护证据并记账已执行版本。
/// </summary>
/// <remarks>
/// <para>脚本按 <c>Migrations.SqlServer</c> 与 <c>Migrations.MySql</c> 子目录成对存放，
/// 本类通过 <see cref="MigrationAssembly"/> 获取承载脚本的程序集并按 Provider 片段过滤；
/// 任一脚本失败立即抛出异常并停止后续脚本，已记账的脚本不会被回滚或重复执行。</para>
/// <para>UUID Binary Contract 与 Pre-V1 Naming Contract 的维护证据以 SQL 变量形式注入，
/// 默认全部关闭；只有显式提供 <see cref="UuidBinaryContractOptions"/> /
/// <see cref="PreV1NamingContractOptions"/> 维护证据时，破坏性 DDL 才能在脚本内通过门禁。
/// API 与 Worker 不得继承这些豁免，也不得引用迁移器或调用本类。</para>
/// <para>MySQL 迁移完成后会校验 <c>fn_uuid_contract_state.SchemaMode</c> 与配置的
/// <c>MySqlGuidStorageMode</c> 是否一致，避免应用层与数据库 Contract 状态漂移。</para>
/// </remarks>
public sealed class DbUpMigrationRunner : IDatabaseMigrationRunner
{
    private readonly IOptions<DatabaseOptions> _databaseOptions;
    private readonly ILoggerFactory _loggerFactory;
    private readonly UuidBinaryContractOptions _contractOptions;
    private readonly PreV1NamingContractOptions _namingContractOptions;

    /// <summary>
    /// 初始化迁移器并使用默认关闭的 Contract 维护证据；适用于不需要执行破坏性 Contract 迁移的常规升级。
    /// </summary>
    /// <param name="databaseOptions">数据库连接、Provider 与命令超时等配置。</param>
    /// <param name="loggerFactory">用于桥接 DbUp 日志到 .NET 日志管道的工厂。</param>
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

    /// <summary>
    /// 初始化迁移器并提供 UUID Binary Contract 维护证据；用于需要执行 009 Contract 切换的维护窗口。
    /// </summary>
    /// <param name="databaseOptions">数据库连接、Provider 与命令超时等配置。</param>
    /// <param name="loggerFactory">用于桥接 DbUp 日志到 .NET 日志管道的工厂。</param>
    /// <param name="contractOptions">UUID Binary Contract 维护证据；默认全部关闭。</param>
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

    /// <summary>
    /// 初始化迁移器并同时提供 UUID Binary 与 Pre-V1 Naming 两种 Contract 维护证据，覆盖全部破坏性迁移门禁。
    /// </summary>
    /// <param name="databaseOptions">数据库连接、Provider 与命令超时等配置。</param>
    /// <param name="loggerFactory">用于桥接 DbUp 日志到 .NET 日志管道的工厂。</param>
    /// <param name="contractOptions">UUID Binary Contract 维护证据。</param>
    /// <param name="namingContractOptions">Pre-V1 Naming Contract 维护证据。</param>
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

    /// <summary>
    /// 按当前 Provider 执行未记账迁移脚本，注入 Contract 维护证据并按需验证 MySQL Schema 模式。
    /// </summary>
    /// <param name="cancellationToken">用于取消迁移的令牌；DbUp 内部按脚本粒度检查取消，已记账脚本不会回滚。</param>
    /// <returns>包含成功标志与本次执行脚本数的迁移结果。</returns>
    /// <exception cref="InvalidOperationException">迁移失败、Contract 证据格式无效或 MySQL Schema 模式与应用配置不一致时抛出。</exception>
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
