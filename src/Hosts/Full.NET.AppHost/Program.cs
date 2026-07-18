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
    .WaitForCompletion(migrator);

builder
    .AddProject<Projects.Full_NET_Host_Worker>("worker")
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment("Database__Provider", provider)
    .WithEnvironment("Database__MySqlGuidStorageMode", "Binary16")
    .WaitForCompletion(migrator);

builder.Build().Run();
