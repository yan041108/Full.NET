using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var redis = builder.AddRedis("redis");
var useMySql = builder.Configuration.GetValue<bool>("UseMySql");
var bootstrapUsername = builder.AddParameter("identity-bootstrap-username");
var bootstrapPassword = builder.AddParameter(
    "identity-bootstrap-password",
    secret: true);
var uuidContractMaintenanceMode = builder.AddParameter(
    "uuid-contract-maintenance-mode");
var uuidContractBackupVerified = builder.AddParameter(
    "uuid-contract-backup-verified");
var uuidContractLegacyWritersStopped = builder.AddParameter(
    "uuid-contract-legacy-writers-stopped");
var uuidContractApprovalId = builder.AddParameter(
    "uuid-contract-ddl-approval-id");

IResourceBuilder<IResourceWithConnectionString> database = useMySql
    ? builder.AddMySql("mysql").AddDatabase("fullnet")
    : builder.AddSqlServer("sql").AddDatabase("fullnet");

var provider = useMySql ? "MySql" : "SqlServer";
var migrator = builder
    .AddProject<Projects.Full_NET_Host_Migrator>("migrator")
    .WithReference(database)
    .WithEnvironment("Database__Provider", provider)
    .WithEnvironment("Database__MySqlGuidStorageMode", "Binary16")
    .WithEnvironment(
        "UuidBinaryContract__MaintenanceMode",
        uuidContractMaintenanceMode)
    .WithEnvironment(
        "UuidBinaryContract__BackupVerified",
        uuidContractBackupVerified)
    .WithEnvironment(
        "UuidBinaryContract__LegacyWritersStopped",
        uuidContractLegacyWritersStopped)
    .WithEnvironment(
        "UuidBinaryContract__DestructiveDdlApprovalId",
        uuidContractApprovalId)
    .WithEnvironment("Identity__Bootstrap__Username", bootstrapUsername)
    .WithEnvironment("Identity__Bootstrap__Password", bootstrapPassword)
    .WithArgs("--seed", "development")
    .WaitFor(database);

builder
    .AddProject<Projects.Full_NET_Host_Api>("api")
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment("Database__Provider", provider)
    .WithEnvironment("Database__MySqlGuidStorageMode", "Binary16")
    // 本地 Aspire 仍共用一个 Redis；生产隔离由显式 Cache/Realtime 连接串门禁强制。
    .WithEnvironment("Realtime__AllowSharedRedisInDevelopment", "true")
    .WaitForCompletion(migrator);

builder
    .AddProject<Projects.Full_NET_Host_Worker>("worker")
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment("Database__Provider", provider)
    .WithEnvironment("Database__MySqlGuidStorageMode", "Binary16")
    .WithEnvironment("Realtime__AllowSharedRedisInDevelopment", "true")
    .WaitForCompletion(migrator);

builder.Build().Run();

/// <summary>
/// Full.NET 本地开发 Aspire 编排入口；按依赖顺序启动资源与项目。
/// </summary>
/// <remarks>
/// 编排拓扑：先就绪 <c>redis</c> 与数据库（<c>sql</c>/<c>mysql</c> 由 <c>UseMySql</c> 切换），
/// <c>migrator</c> <c>WaitFor(database)</c>，<c>api</c> 与 <c>worker</c> 均 <c>WaitForCompletion(migrator)</c>，
/// 确保迁移与 Seed 完成后才启动业务进程。本地共用一个 Redis，生产 Cache/Realtime 隔离由显式连接串门禁强制。
/// </remarks>
public partial class Program;
